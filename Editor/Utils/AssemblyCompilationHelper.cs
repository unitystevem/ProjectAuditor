using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
#if UNITY_2018_2_OR_NEWER
using UnityEditor.Build.Player;
using UnityEngine;

#endif

namespace Unity.ProjectAuditor.Editor.Utils
{
    enum CompilationStatus
    {
        NotStarted,
        Started,
        Finished,
        FinishedWithErrors
    }

    class CompilationResult
    {
        public AssemblyInfo AssemblyInfo;
        public CompilationStatus Status;
        public CompilerMessage[] CompilerMessages;
    }

    class AssemblyCompilationHelper : IDisposable
    {
        string m_OutputFolder = string.Empty;
        Dictionary<string, CompilationResult> m_CompilationResults;
        Action<string> m_OnAssemblyCompilationStarted;

        public void Dispose()
        {
#if UNITY_2018_2_OR_NEWER
            if (m_OnAssemblyCompilationStarted != null)
                CompilationPipeline.assemblyCompilationStarted -= m_OnAssemblyCompilationStarted;

            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
#endif
            if (!string.IsNullOrEmpty(m_OutputFolder) && Directory.Exists(m_OutputFolder))
            {
                Directory.Delete(m_OutputFolder, true);
            }
            m_OutputFolder = string.Empty;
        }

        public CompilationResult[] Compile(bool editorAssemblies = false, IProgressBar progressBar = null)
        {
#if UNITY_2019_3_OR_NEWER
            var assemblies = CompilationPipeline.GetAssemblies(editorAssemblies ? AssembliesType.Editor : AssembliesType.PlayerWithoutTestAssemblies);
#elif UNITY_2018_1_OR_NEWER
            var assemblies = CompilationPipeline.GetAssemblies(editorAssemblies ? AssembliesType.Editor : AssembliesType.Player);
#else
            var assemblies = CompilationPipeline.GetAssemblies();
#endif

#if UNITY_2018_2_OR_NEWER
            if (editorAssemblies)
            {
                CompileEditorAssemblies(assemblies, false);
            }
            else
            {
                CompilePlayerAssemblies(assemblies, progressBar);
            }
#else
            // fallback to CompilationPipeline assemblies
            CompileEditorAssemblies(assemblies, !editorAssemblies);
#endif

            if (m_CompilationResults.Any(pair => pair.Value.Status != CompilationStatus.Finished))
            {
                Dispose();
                throw new AssemblyCompilationException();
            }

            foreach (var pair in m_CompilationResults)
            {
                var assemblyInfo = AssemblyHelper.GetAssemblyInfoFromAssemblyPath(pair.Key);
                var assembly = assemblies.First(a => a.name.Equals(assemblyInfo.name));
                var sourcePaths = assembly.sourceFiles.Select(file => file.Remove(0, assemblyInfo.relativePath.Length + 1));

                assemblyInfo.sourcePaths = sourcePaths.ToArray();
                pair.Value.AssemblyInfo = assemblyInfo;
            }

            return m_CompilationResults.Select(pair => pair.Value).ToArray();
        }

        void CompileEditorAssemblies(IEnumerable<Assembly> assemblies, bool excludeEditorOnlyAssemblies)
        {
            if (excludeEditorOnlyAssemblies)
            {
                assemblies = assemblies.Where(a => a.flags != AssemblyFlags.EditorAssembly);
            }

            m_CompilationResults = assemblies.ToDictionary(assembly => assembly.outputPath, assembly => new CompilationResult
            {
                Status = CompilationStatus.Finished
            });
        }
#if UNITY_2018_2_OR_NEWER
        void CompilePlayerAssemblies(Assembly[] assemblies, IProgressBar progressBar = null)
        {
            if (progressBar != null)
            {
                var numAssemblies = assemblies.Length;
                progressBar.Initialize("Assembly Compilation", "Compiling project scripts",
                    numAssemblies);
                m_OnAssemblyCompilationStarted = (outputAssemblyPath) =>
                {
                    // The compilation pipeline might compile Editor-specific assemblies
                    // let's advance the progress bar only for Player ones.
                    var filename = Path.GetFileName(outputAssemblyPath);
                    var assemblyName = Path.GetFileNameWithoutExtension(filename);
                    if (assemblies.FirstOrDefault(asm => asm.name.Equals(assemblyName)) != null)
                    {
                        m_CompilationResults[outputAssemblyPath].Status = CompilationStatus.Started;

                        progressBar.AdvanceProgressBar(assemblyName);
                    }

                };
                CompilationPipeline.assemblyCompilationStarted += m_OnAssemblyCompilationStarted;
            }
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;

            m_OutputFolder = FileUtil.GetUniqueTempPathInProject();

            m_CompilationResults = assemblies.ToDictionary(a => Path.Combine(m_OutputFolder, Path.GetFileName(a.outputPath)).Replace("\\", "/"), a => new CompilationResult
            {
                Status = CompilationStatus.NotStarted
            });

            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            var input = new ScriptCompilationSettings
            {
                target = buildTarget,
                @group =  BuildPipeline.GetBuildTargetGroup(buildTarget)
            };

            /*var result = */PlayerBuildInterface.CompilePlayerScripts(input, m_OutputFolder);

            if (progressBar != null)
                progressBar.ClearProgressBar();
        }
#endif
        public IEnumerable<string> GetCompiledAssemblyDirectories()
        {
#if UNITY_2018_2_OR_NEWER
            yield return m_OutputFolder;
#else
            foreach (var dir in CompilationPipeline.GetAssemblies()
                     .Where(a => a.flags != AssemblyFlags.EditorAssembly).Select(assembly => Path.GetDirectoryName(assembly.outputPath)).Distinct())
            {
                yield return dir;
            }
#endif
        }

        void OnAssemblyCompilationFinished(string outputAssemblyPath, CompilerMessage[] messages)
        {
            var compilationResult = m_CompilationResults[outputAssemblyPath];
            compilationResult.CompilerMessages = messages;
            compilationResult.Status = messages.Any(m => m.type == CompilerMessageType.Error) ? CompilationStatus.FinishedWithErrors : CompilationStatus.Finished;
        }
    }
}
