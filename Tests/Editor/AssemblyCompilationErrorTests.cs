using System.Linq;
using NUnit.Framework;
using Unity.ProjectAuditor.Editor.Utils;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityEditor.ProjectAuditor.EditorTests
{
    public class AssemblyCompilationErrorTests
    {
        TempAsset m_TempScript;

        string m_CompileErrorMessage = "error CS1519: Invalid token '}' in class, struct, or interface member declaration";

        [OneTimeSetUp]
        public void SetUp()
        {
            m_TempScript = new TempAsset("MyClassWithError.cs", @"
class MyClassWithError {
#if !UNITY_EDITOR
    asd
#endif
}
");
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            TempAsset.Cleanup();
        }

        [Test]
        [ExplicitAttribute]
        public void ErrorIsReportedAsCompilerMessage()
        {
            LogAssert.ignoreFailingMessages = true;

            using (var compilationHelper = new AssemblyCompilationHelper())
            {
                var results = compilationHelper.Compile();
                var resultsWithErrors = results.Where(r => r.Status == CompilationStatus.FinishedWithErrors);
                Assert.AreEqual(1, resultsWithErrors.Count());

                var errorMessages = resultsWithErrors.First().CompilerMessages.Where(m => m.type == CompilerMessageType.Error);
                Assert.AreEqual(1, errorMessages.Count());

                Assert.True(errorMessages.First().message.Contains(m_CompileErrorMessage));
            }

            LogAssert.Expect(LogType.Error, "Failed to compile player scripts");
            LogAssert.ignoreFailingMessages = false;
        }
    }
}
