using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.ProjectAuditor.Editor.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.ProjectAuditor.Editor.Auditors
{
    public class AssetsAuditor : IAuditor
    {
        static readonly ProblemDescriptor k_Descriptor = new ProblemDescriptor
            (
            302000,
            "Resources folder asset & dependencies",
            Area.BuildSize,
            "The Resources folder is a common source of many problems in Unity projects. Improper use of the Resources folder can bloat the size of a project’s build, lead to uncontrollable excessive memory utilization, and significantly increase application startup times.",
            "Use AssetBundles when possible"
            );

        static Dictionary<string, DependencyNode> m_AssetsDictionary;

        readonly List<ProblemDescriptor> m_ProblemDescriptors = new List<ProblemDescriptor>();

        public IEnumerable<ProblemDescriptor> GetDescriptors()
        {
            return m_ProblemDescriptors;
        }

        public void Initialize(ProjectAuditorConfig config)
        {
            RegisterDescriptor(k_Descriptor);
        }

        public void Reload(string path)
        {
        }

        public void RegisterDescriptor(ProblemDescriptor descriptor)
        {
            m_ProblemDescriptors.Add(descriptor);
        }

        public void Audit(Action<ProjectIssue> onIssueFound, Action onComplete, IProgressBar progressBar = null)
        {
            BuildAssetsDictionary(onIssueFound);
            onComplete();
        }

        static void BuildAssetsDictionary(Action<ProjectIssue> onIssueFound)
        {
            var allAssetPaths = AssetDatabase.GetAllAssetPaths();
            var assetPathsDict = new Dictionary<string, DependencyNode>();
            foreach (var assetPath in allAssetPaths)
            {
                if ((File.GetAttributes(assetPath) & FileAttributes.Directory) == FileAttributes.Directory)
                    continue;

                var isResource = assetPath.IndexOf("/resources/", StringComparison.OrdinalIgnoreCase) >= 0 && assetPath.IndexOf("/editor/", StringComparison.OrdinalIgnoreCase) == -1;
                var root = AnalyzeAssetAndDependencies(assetPath, assetPathsDict, onIssueFound, null, isResource);
                var dependencies = AssetDatabase.GetDependencies(assetPath, true);
                foreach (var depAssetPath in dependencies)
                {
                    // skip self
                    if (depAssetPath.Equals(assetPath))
                        continue;

                    AnalyzeAssetAndDependencies(depAssetPath, assetPathsDict, onIssueFound, root, isResource);
                }
            }

            m_AssetsDictionary = assetPathsDict;
        }

        static DependencyNode AnalyzeAssetAndDependencies(
            string assetPath, Dictionary<string, DependencyNode> assetPathsDict, Action<ProjectIssue> onIssueFound, DependencyNode parent, bool isResource)
        {
            // skip C# scripts
            if (Path.GetExtension(assetPath).Equals(".cs"))
                return null;

            if (assetPathsDict.ContainsKey(assetPath))
            {
                var dep = assetPathsDict[assetPath];
                if (parent != null)
                    dep.AddChild(parent);
                return dep;
            }

            var location = new Location(assetPath);
            var dependencyNode = new AssetDependencyNode
            {
                location = new Location(assetPath)
            };
            if (parent != null)
                dependencyNode.AddChild(parent);

            if (isResource)
                onIssueFound(new ProjectIssue
                    (
                        k_Descriptor,
                        Path.GetFileNameWithoutExtension(location.Path),
                        IssueCategory.Assets,
                        location
                    )
                    {
                        dependencies = dependencyNode
                    }
                );

            assetPathsDict.Add(assetPath, dependencyNode);

            return dependencyNode;
        }

        public static DependencyNode GetAssetNode(string assetPath)
        {
            if (m_AssetsDictionary == null)
                return null;

            if (!m_AssetsDictionary.ContainsKey(assetPath))
                return null;

            return m_AssetsDictionary[assetPath];
        }
    }
}
