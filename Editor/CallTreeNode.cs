using System;
using System.Collections.Generic;
using Mono.Cecil;

namespace Unity.ProjectAuditor.Editor
{
    public class CallTreeNode
    {
        public readonly string name;
        public readonly string typeName;
        public readonly string methodName;

        public List<CallTreeNode> children = new List<CallTreeNode>();

        public string prettyName
        {
            get
            {
                if (string.IsNullOrEmpty(typeName))
                    return name;
                return typeName + "." + methodName;
            }
        }
        
        public CallTreeNode(string _name, CallTreeNode caller = null)
        {
            name = _name;

            typeName = String.Empty;
            methodName = String.Empty;

            if (caller != null)
                children.Add(caller); 
        }
        
        public CallTreeNode(MethodReference methodReference, CallTreeNode caller = null)
        {
            name = methodReference.FullName;
            methodName = "Anonymous"; // default value
            
            // check if it's a coroutine
            if (name.IndexOf("/<") >= 0)
            {
                var fullName = methodReference.DeclaringType.FullName;
                var methodStartIndex = fullName.IndexOf("<") + 1;
                if (methodStartIndex > 0)
                {
                    var length = fullName.IndexOf(">") - methodStartIndex;
                    typeName = fullName.Substring(0, fullName.IndexOf("/"));
                    if (length > 0)
                        methodName = fullName.Substring(methodStartIndex, length);
                }
                else
                {
                    // for some reason, some generated types don't have the same syntax
                    typeName = fullName;
                }
            }
            else
            {
                typeName = methodReference.DeclaringType.Name;
                methodName = methodReference.Name;
            }

            if (caller != null)
                children.Add(caller); 
        }

        public CallTreeNode GetChild(int index = 0)
        {
            return children[0];
        }
    }
}