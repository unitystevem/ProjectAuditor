using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.ProjectAuditor.Editor.UI
{
    class AnalysisWindow : EditorWindow
    {
        protected AnalysisView m_AnalysisView;
        protected List<ProjectIssue> m_Issues;

        public static T FindOpenWindow<T>() where T : class
        {
            Object[] windows = Resources.FindObjectsOfTypeAll(typeof(T));
            if (windows != null && windows.Length > 0)
                return windows[0] as T;

            return null;
        }

        public AnalysisWindow()
        {
            m_AnalysisView = new AnalysisView();
        }

        public virtual void CreateTable(AnalysisViewDescriptor desc, ProjectAuditorConfig config, Preferences prefs, IProjectIssueFilter filter)
        {
            m_AnalysisView.CreateTable(desc, config, prefs, filter);
            m_Issues = new List<ProjectIssue>();
        }

        public void AddIssues(IEnumerable<ProjectIssue> issues)
        {
            m_AnalysisView.AddIssues(issues);
            m_Issues.AddRange(issues);
        }

        public void Refresh()
        {
            m_AnalysisView.Refresh();
        }

        public void Clear()
        {
            m_AnalysisView.Clear();
            m_Issues.Clear();
        }

        public bool IsValid()
        {
            return m_AnalysisView.IsValid();
        }

        public virtual void OnGUI()
        {
            if (!m_AnalysisView.IsValid())
            {
                Close();
                return;
            }

            m_AnalysisView.OnGUI();
        }
    }
}
