using System;
using System.Collections.Generic;
using System.Linq;
using Unity.ProjectAuditor.Editor.CodeAnalysis;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Profiling;

namespace Unity.ProjectAuditor.Editor.UI
{
    class IssueTable : TreeView
    {
        public enum ColumnType
        {
            Description = 0,
            Severity,
            Area,
            Path,
            Filename,
            FileType,
            Custom,

            Count
        }

        static readonly int k_FirstId = 1;
        static readonly string k_InfoIconName = "console.infoicon";
        static readonly string k_WarnIconName = "console.warnicon";
        static readonly string k_ErrorIconName = "console.erroricon";

        static GUIStyle s_LabelStyle;

        readonly ProjectAuditorConfig m_Config;
        readonly AnalysisViewDescriptor m_Desc;
        readonly IProjectIssueFilter m_Filter;
        readonly List<TreeViewItem> m_Rows = new List<TreeViewItem>(100);

        List<IssueTableItem> m_TreeViewItemGroups;
        IssueTableItem[] m_TreeViewItemIssues;
        int m_NextId;
        int m_NumMatchingIssues;
        bool m_FlatView;
        int m_FontSize;

        public IssueTable(TreeViewState state, MultiColumnHeader multicolumnHeader,
                          AnalysisViewDescriptor desc, ProjectAuditorConfig config,
                          IProjectIssueFilter filter) : base(state,
                                                             multicolumnHeader)
        {
            m_Config = config;
            m_Filter = filter;
            m_Desc = desc;
            m_FlatView = !desc.groupByDescription;
            m_NextId = k_FirstId;
            m_FontSize = Preferences.k_MinFontSize;
            multicolumnHeader.sortingChanged += OnSortingChanged;
        }

        public void AddIssues(ProjectIssue[] issues)
        {
            if (m_Desc.groupByDescription)
            {
                var descriptors = issues.Select(i => i.descriptor).Distinct();
                if (m_TreeViewItemGroups == null)
                {
                    m_TreeViewItemGroups = new List<IssueTableItem>(descriptors.Count());
                }

                foreach (var descriptor in descriptors)
                {
                    var groupItem = new IssueTableItem(m_NextId++, 0, descriptor);
                    m_TreeViewItemGroups.Add(groupItem);
                }
            }

            var itemsList = new List<IssueTableItem>(issues.Length);
            if (m_TreeViewItemIssues != null)
                itemsList.AddRange(m_TreeViewItemIssues);
            foreach (var issue in issues)
            {
                var depth = m_Desc.groupByDescription ? 1 : 0;
                var item = new IssueTableItem(m_NextId++, depth, issue.name, issue.descriptor, issue);
                itemsList.Add(item);
            }

            m_TreeViewItemIssues = itemsList.ToArray();
        }

        public void Clear()
        {
            m_NextId = k_FirstId;
            if (m_TreeViewItemGroups != null)
                m_TreeViewItemGroups.Clear();
            m_TreeViewItemIssues = null;
        }

        public void SetFlatView(bool value)
        {
            m_FlatView = value;
        }

        protected override TreeViewItem BuildRoot()
        {
            var idForHiddenRoot = -1;
            var depthForHiddenRoot = -1;
            var root = new TreeViewItem(idForHiddenRoot, depthForHiddenRoot, "root");

            if (m_Desc.groupByDescription)
            {
                foreach (var item in m_TreeViewItemGroups)
                {
                    root.AddChild(item);
                }
            }
            else
            {
                foreach (var item in m_TreeViewItemIssues)
                {
                    root.AddChild(item);
                }
            }

            return root;
        }

        protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
        {
            m_Rows.Clear();

            // find all issues matching the filters and make an array out of them
            Profiler.BeginSample("IssueTable.Match");
            var filteredItems = m_TreeViewItemIssues.Where(item =>
            {
                return m_Filter.Match(item.ProjectIssue);
            }).ToArray();

            Profiler.EndSample();

            m_NumMatchingIssues = filteredItems.Length;
            if (m_NumMatchingIssues == 0)
            {
                m_Rows.Add(new TreeViewItem(0, 0, "No items"));
                return m_Rows;
            }

            Profiler.BeginSample("IssueTable.BuildRows");
            if (m_Desc.groupByDescription && !hasSearch && !m_FlatView)
            {
                var descriptors = filteredItems.Select(i => i.ProblemDescriptor).Distinct();
                foreach (var descriptor in descriptors)
                {
                    var group = m_TreeViewItemGroups.Find(g => g.ProblemDescriptor.Equals(descriptor));
                    m_Rows.Add(group);

                    var groupIsExpanded = state.expandedIDs.Contains(group.id);
                    var children = filteredItems.Where(item => item.ProblemDescriptor.Equals(descriptor));

                    group.displayName = string.Format("{0} ({1})", descriptor.description, children.Count());
                    if (group.children != null)
                        group.children.Clear();

                    foreach (var child in children)
                    {
                        if (groupIsExpanded)
                            m_Rows.Add(child);
                        group.AddChild(child);
                    }
                }
            }
            else
            {
                foreach (var item in filteredItems)
                {
                    m_Rows.Add(item);
                }
            }
            SortIfNeeded(m_Rows);

            Profiler.EndSample();

            return m_Rows;
        }

        protected override IList<int> GetAncestors(int id)
        {
            if (m_TreeViewItemIssues == null || m_TreeViewItemIssues.Length == 0)
                return new List<int>();
            return base.GetAncestors(id);
        }

        protected override IList<int> GetDescendantsThatHaveChildren(int id)
        {
            if (m_TreeViewItemIssues == null || m_TreeViewItemIssues.Length == 0)
                return new List<int>();
            return base.GetDescendantsThatHaveChildren(id);
        }

        public void SetFontSize(int fontSize)
        {
            const int k_DefaultRowHeight = 18;

            m_FontSize = fontSize;
            rowHeight = k_DefaultRowHeight * fontSize / Preferences.k_MinFontSize;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            for (var i = 0; i < args.GetNumVisibleColumns(); ++i)
                CellGUI(args.GetCellRect(i), args.item, args.GetColumn(i), ref args);
        }

        void CellGUI(Rect cellRect, TreeViewItem treeViewItem, int columnIndex, ref RowGUIArgs args)
        {
            var column = m_Desc.columnTypes[columnIndex];

            // indent first column, if necessary
            if (m_Desc.groupByDescription && (int)ColumnType.Description == column)
            {
                var indent = GetContentIndent(treeViewItem) + extraSpaceBeforeIconAndLabel;
                cellRect.xMin += indent;
                CenterRectUsingSingleLineHeight(ref cellRect);
            }

            if (s_LabelStyle == null)
                s_LabelStyle = new GUIStyle(EditorStyles.label);
            s_LabelStyle.fontSize = m_FontSize;

            var item = treeViewItem as IssueTableItem;
            if (item == null)
            {
                if (column == ColumnType.Description)
                    EditorGUI.LabelField(cellRect, new GUIContent(treeViewItem.displayName, treeViewItem.displayName), s_LabelStyle);
                return;
            }

            var issue = item.ProjectIssue;
            var descriptor = item.ProblemDescriptor;
            var areaLongDescription = "This issue might have an impact on " + descriptor.area;

            var rule = m_Config.GetRule(descriptor, issue != null ? issue.GetCallingMethod() : string.Empty);
            if (rule == null && issue != null)
                rule = m_Config.GetRule(descriptor); // try to find non-specific rule
            if (rule != null && rule.severity == Rule.Severity.None)
                GUI.enabled = false;

            if (item.IsGroup())
                switch (column)
                {
                    case ColumnType.Description:
                        EditorGUI.LabelField(cellRect, new GUIContent(item.GetDisplayName(), item.GetDisplayName()), s_LabelStyle);
                        break;
                    case ColumnType.Area:
                        EditorGUI.LabelField(cellRect, new GUIContent(descriptor.area, areaLongDescription), s_LabelStyle);
                        break;
                }
            else
                switch (column)
                {
                    case ColumnType.Severity:
                    {
                        string iconName = string.Empty;
                        string tooltip = string.Empty;
                        if (issue.category == IssueCategory.Code && issue.isPerfCriticalContext)
                        {
                            iconName = k_WarnIconName;
                            tooltip = "Performance Critical Context";
                        }
                        else
                        {
                            switch (issue.descriptor.severity)
                            {
                                case Rule.Severity.Info:
                                    iconName = k_InfoIconName;
                                    tooltip = "Info";
                                    break;
                                case Rule.Severity.Warning:
                                    iconName = k_WarnIconName;
                                    tooltip = "Warning";
                                    break;
                                case Rule.Severity.Error:
                                    iconName = k_ErrorIconName;
                                    tooltip = "Error";
                                    break;
                                default:
                                    iconName = string.Empty;
                                    tooltip = string.Empty;
                                    break;
                            }
                        }
                        if (!string.IsNullOrEmpty(iconName))
                        {
#if UNITY_2018_3_OR_NEWER
                            EditorGUI.LabelField(cellRect, EditorGUIUtility.TrIconContent(iconName, tooltip), s_LabelStyle);
#else
                            EditorGUI.LabelField(cellRect, new GUIContent(EditorGUIUtility.FindTexture(iconName), tooltip), s_LabelStyle);
#endif
                        }
                    }
                    break;
                    case ColumnType.Area:
                        if (!m_Desc.groupByDescription)
                            EditorGUI.LabelField(cellRect, new GUIContent(descriptor.area, areaLongDescription), s_LabelStyle);
                        break;
                    case ColumnType.Description:
                        if (m_Desc.groupByDescription)
                        {
                            var text = item.GetDisplayName();
                            var tooltip = issue.GetCallingMethod();
                            var guiContent = new GUIContent(text, tooltip);

#if UNITY_2018_3_OR_NEWER
                            if (m_Desc.descriptionWithIcon && issue.location != null)
                            {
                                var icon = AssetDatabase.GetCachedIcon(issue.location.Path);
                                guiContent = EditorGUIUtility.TrTextContentWithIcon(text, tooltip, icon);
                            }
#endif
                            EditorGUI.LabelField(cellRect, guiContent, s_LabelStyle);
                        }
                        else if (string.IsNullOrEmpty(descriptor.problem))
                        {
                            if (issue.location != null)
                            {
                                EditorGUI.LabelField(cellRect,
                                    new GUIContent(item.GetDisplayName(), issue.location.Path), s_LabelStyle);
                            }
                            else
                                EditorGUI.LabelField(cellRect, item.GetDisplayName(), s_LabelStyle);
                        }
                        else
                        {
                            EditorGUI.LabelField(cellRect, new GUIContent(item.GetDisplayName(), descriptor.problem), s_LabelStyle);
                        }

                        break;
                    case ColumnType.Filename:
                        if (issue.filename != string.Empty)
                        {
                            var filename = string.Format("{0}", issue.filename);
                            if (issue.category == IssueCategory.Code)
                                filename += string.Format(":{0}", issue.line);

                            // display fullpath as tooltip
                            EditorGUI.LabelField(cellRect, new GUIContent(filename, issue.relativePath), s_LabelStyle);
                        }
                        break;

                    case ColumnType.Path:
                        if (issue.location != null)
                        {
                            var path = string.Format("{0}", issue.location.Path);
                            if (issue.category == IssueCategory.Code)
                                path += string.Format(":{0}", issue.line);

                            EditorGUI.LabelField(cellRect, new GUIContent(path), s_LabelStyle);
                        }
                        break;

                    case ColumnType.FileType:
                        if (issue.location.Path != string.Empty)
                        {
                            var ext = issue.location.Extension;
                            if (ext.StartsWith("."))
                                ext = ext.Substring(1);
                            EditorGUI.LabelField(cellRect, new GUIContent(ext), s_LabelStyle);
                        }

                        break;
                    default:
                        var propertyIndex = column - ColumnType.Custom;
                        var property = issue.GetCustomProperty(propertyIndex);
                        if (property != string.Empty)
                        {
                            var desc = m_Desc.customColumnDescriptors[propertyIndex];
                            if (desc.Format == PropertyFormat.Bool)
                                EditorGUI.Toggle(cellRect, property.Equals(true.ToString()));
                            else
                                EditorGUI.LabelField(cellRect, new GUIContent(property), s_LabelStyle);
                        }

                        break;
                }

            if (rule != null && rule.severity == Rule.Severity.None)
                GUI.enabled = true;
        }

        new void CenterRectUsingSingleLineHeight(ref Rect rect)
        {
            float singleLineHeight = rowHeight;
            if (rect.height > singleLineHeight)
            {
                rect.y += (rect.height - singleLineHeight) * 0.5f;
                rect.height = singleLineHeight;
            }
        }

        protected override void DoubleClickedItem(int id)
        {
            var rows = FindRows(new[] {id});
            var item = rows.FirstOrDefault();

            if (item == null)
                return;

            var tableItem = item as IssueTableItem;

            if (tableItem == null)
                return;
            if (item.hasChildren)
            {
                if (m_Desc.onOpenDescriptor != null)
                {
                    m_Desc.onOpenDescriptor(tableItem.ProblemDescriptor);
                }
                return;
            }

            var issue = tableItem.ProjectIssue;
            if (issue.location != null && issue.location.IsValid())
            {
                m_Desc.onDoubleClick(issue.location);
            }
        }

        protected override void SearchChanged(string newSearch)
        {
            // auto-expand groups containing selected items
            foreach (var id in state.selectedIDs)
            {
                var item = m_TreeViewItemIssues.FirstOrDefault(issue => issue.id == id && issue.parent != null);
                if (item != null && !state.expandedIDs.Contains(item.parent.id))
                {
                    state.expandedIDs.Add(item.parent.id);
                }
            }
        }

        public int GetNumMatchingIssues()
        {
            return m_NumMatchingIssues;
        }

        public IssueTableItem[] GetSelectedItems()
        {
            var ids = GetSelection();
            if (ids.Count() > 0)
                return FindRows(ids).OfType<IssueTableItem>().ToArray();

            return new IssueTableItem[0];
        }

        void OnSortingChanged(MultiColumnHeader _multiColumnHeader)
        {
            SortIfNeeded(GetRows());
        }

        void SortIfNeeded(IList<TreeViewItem> rows)
        {
            if (rows.Count <= 1) return;

            if (multiColumnHeader.sortedColumnIndex == -1)
                return; // No column to sort for (just use the order the data are in)

            SortByMultipleColumns(rows);
            Repaint();
        }

        void SortByMultipleColumns(IList<TreeViewItem> rows)
        {
            var sortedColumns = multiColumnHeader.state.sortedColumns;
            if (sortedColumns.Length == 0)
                return;

            var columnAscending = new bool[sortedColumns.Length];
            for (var i = 0; i < sortedColumns.Length; i++)
                columnAscending[i] = multiColumnHeader.IsSortedAscending(sortedColumns[i]);

            var root = new ItemTree(null, m_Desc);
            var stack = new Stack<ItemTree>();
            stack.Push(root);
            foreach (var row in rows)
            {
                var r = row as IssueTableItem;
                if (r == null)
                    continue;

                var activeParentDepth = stack.Peek().Depth;

                while (row.depth <= activeParentDepth)
                {
                    stack.Pop();
                    activeParentDepth = stack.Peek().Depth;
                }

                if (row.depth > activeParentDepth)
                {
                    var t = new ItemTree(r, m_Desc);
                    stack.Peek().AddChild(t);
                    stack.Push(t);
                }
            }

            root.Sort(sortedColumns, columnAscending);

            // convert back to rows
            var newRows = new List<TreeViewItem>(rows.Count);
            root.ToList(newRows);
            rows.Clear();
            foreach (var treeViewItem in newRows)
                rows.Add(treeViewItem);
        }

        internal class ItemTree
        {
            readonly List<ItemTree> m_Children;
            readonly IssueTableItem m_Item;
            readonly AnalysisViewDescriptor m_ViewDescriptor;

            public ItemTree(IssueTableItem i, AnalysisViewDescriptor viewDescriptor)
            {
                m_Item = i;
                m_Children = new List<ItemTree>();
                m_ViewDescriptor = viewDescriptor;
            }

            public int Depth
            {
                get { return m_Item == null ? -1 : m_Item.depth; }
            }

            public void AddChild(ItemTree item)
            {
                m_Children.Add(item);
            }

            public void Sort(int[] columnSortOrder, bool[] isColumnAscending)
            {
                m_Children.Sort(delegate(ItemTree a, ItemTree b)
                {
                    var rtn = 0;
                    for (var i = 0; i < columnSortOrder.Length; i++)
                    {
                        IssueTableItem firstItem;
                        IssueTableItem secondItem;

                        if (isColumnAscending[i])
                        {
                            firstItem = a.m_Item;
                            secondItem = b.m_Item;
                        }
                        else
                        {
                            firstItem = b.m_Item;
                            secondItem = a.m_Item;
                        }

                        var firstString = String.Empty;
                        var secondString = String.Empty;

                        var columnEnum = m_ViewDescriptor.columnTypes[columnSortOrder[i]];
                        switch (columnEnum)
                        {
                            case ColumnType.Description:
                                firstString = firstItem.GetDisplayName();
                                secondString = secondItem.GetDisplayName();
                                break;
                            case ColumnType.Area:
                                firstString = firstItem.ProblemDescriptor.area;
                                secondString = secondItem.ProblemDescriptor.area;
                                break;
                            case ColumnType.Filename:
                                firstString = firstItem.ProjectIssue != null
                                    ? firstItem.ProjectIssue.filename
                                    : string.Empty;
                                secondString = secondItem.ProjectIssue != null
                                    ? secondItem.ProjectIssue.filename
                                    : string.Empty;
                                break;
                            case ColumnType.Path:
                                firstString = firstItem.ProjectIssue != null
                                    ? firstItem.ProjectIssue.location.Path
                                    : string.Empty;
                                secondString = secondItem.ProjectIssue != null
                                    ? secondItem.ProjectIssue.location.Path
                                    : string.Empty;
                                break;
                            case ColumnType.FileType:
                                firstString = firstItem.ProjectIssue != null ? firstItem.ProjectIssue.location.Extension : string.Empty;
                                secondString = secondItem.ProjectIssue != null ? secondItem.ProjectIssue.location.Extension : string.Empty;
                                break;
                            case ColumnType.Severity:
                                firstString = firstItem.ProjectIssue != null ? firstItem.ProjectIssue.severity.ToString() : string.Empty;
                                secondString = secondItem.ProjectIssue != null ? secondItem.ProjectIssue.severity.ToString() : string.Empty;
                                break;
                            default:
                                var propertyIndex = columnEnum - ColumnType.Custom;
                                var format = m_ViewDescriptor.customColumnDescriptors[propertyIndex].Format;
                                if (format == PropertyFormat.Integer)
                                {
                                    int first;
                                    int second;
                                    if (!int.TryParse(firstItem.ProjectIssue.GetCustomProperty(propertyIndex), out first))
                                        first = -999999;
                                    if (!int.TryParse(secondItem.ProjectIssue.GetCustomProperty(propertyIndex), out second))
                                        second = -999999;
                                    return first - second;
                                }

                                firstString = firstItem.ProjectIssue != null
                                    ? firstItem.ProjectIssue.GetCustomProperty(propertyIndex)
                                    : string.Empty;
                                secondString = secondItem.ProjectIssue != null
                                    ? secondItem.ProjectIssue.GetCustomProperty(propertyIndex)
                                    : string.Empty;

                                break;
                        }

                        rtn = string.Compare(firstString, secondString, StringComparison.Ordinal);
                        if (rtn == 0)
                            continue;
                        return rtn;
                    }

                    return rtn;
                });

                foreach (var child in m_Children)
                    child.Sort(columnSortOrder, isColumnAscending);
            }

            public void ToList(List<TreeViewItem> list)
            {
                // TODO be good to optimise this, rarely used, so not required
                if (m_Item != null)
                    list.Add(m_Item);
                foreach (var child in m_Children)
                    child.ToList(list);
            }
        }
    }
}
