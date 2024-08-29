using Bakabase.Modules.CustomProperty.Components.Properties.Multilevel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Modules.CustomProperty.Extensions
{
    public static class MultilevelPropertyExtensions
    {
        public static string[]? FindLabelChain(this MultilevelDataOptions options, string id)
        {
            if (options.Value == id)
            {
                return [options.Label];
            }

            return options.Children?.Select(child => child.FindLabelChain(id)).OfType<string[]>()
                .Select(result => new[] {options.Label}.Concat(result).ToArray()).FirstOrDefault();
        }

        public static string[]? FindLabelChain(this List<MultilevelDataOptions> options, string id)
        {
            return options.Select(o => o.FindLabelChain(id)).OfType<string[]>().FirstOrDefault();
        }

        public static List<string?> FindValuesByLabelChains(this List<MultilevelDataOptions> branches,
            List<List<string>> labelChains)
        {
            return labelChains.Select(branches.FindValueByLabelChain).ToList();
        }

        public static string? FindValueByLabelChain(this List<MultilevelDataOptions> branches, List<string> labelChain)
        {
            if (labelChain.Any())
            {
                var label = labelChain[0];
                var branch = branches.FirstOrDefault(x => x.Label == label);
                if (branch != null)
                {
                    if (labelChain.Count == 1)
                    {
                        return branch.Value;
                    }

                    return branch.Children?.FindValueByLabelChain(labelChain.Skip(1).ToList());
                }
            }

            return null;
        }

        public static MultilevelDataOptions? FindNode(this MultilevelDataOptions branch,
            Func<MultilevelDataOptions, bool> find)
        {
            if (find(branch))
            {
                return branch;
            }

            return branch.Children?.Select(subBranch => subBranch.FindNode(find)).OfType<MultilevelDataOptions>()
                .FirstOrDefault();
        }

        public static MultilevelDataOptions? FindFirstNode(this List<MultilevelDataOptions> branches,
            Func<MultilevelDataOptions, bool> find)
        {
            return branches.Select(branch => branch.FindNode(find)).OfType<MultilevelDataOptions>().FirstOrDefault();
        }

        public static IEnumerable<string> ExtractValues(this List<MultilevelDataOptions> nodes, bool leafOnly)
        {
            foreach (var node in nodes)
            {
                if (!leafOnly || node.Children == null)
                {
                    yield return node.Value;
                }

                if (node.Children != null)
                {
                    foreach (var v in node.Children.ExtractValues(leafOnly))
                    {
                        yield return v;
                    }
                }
            }
        }

        public static List<(List<string> Branch, string Value)> ExtractBranches(this List<MultilevelDataOptions> nodes,
            bool includeIntermediate)
        {
            var branches = new List<(List<string> Branch, string Value)>();
            foreach (var node in nodes)
            {
                if (node.Children != null)
                {
                    if (includeIntermediate)
                    {
                        branches.Add(([node.Label], node.Value));
                    }

                    var childrenBranches = node.Children.ExtractBranches(includeIntermediate);
                    foreach (var cb in childrenBranches)
                    {
                        branches.Add(([node.Label, .. cb.Branch], cb.Value));
                    }
                }
                else
                {
                    branches.Add(([node.Label], node.Value));
                }
            }

            return branches;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="options"></param>
        /// <param name="bizValueBranches"></param>
        /// <returns>Whether options have been changed.</returns>
        public static bool AddBranchOptions(this MultilevelPropertyOptions options,
            List<List<string>>? bizValueBranches)
        {
            var optionsChanged = false;
            if (bizValueBranches != null && options.AllowAddingNewDataDynamically)
            {
                foreach (var bizValueBranch in bizValueBranches)
                {
                    options.Data ??= [];
                    if (AddBranchOptions(options.Data, bizValueBranch))
                    {
                        optionsChanged = true;
                    }
                }
            }

            return optionsChanged;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="options"></param>
        /// <param name="bizValueBranch"></param>
        /// <param name="index"></param>
        /// <returns>Whether options have been changed.</returns>
        private static bool AddBranchOptions(this List<MultilevelDataOptions> options, List<string> bizValueBranch,
            int index = 0)
        {
            if (index >= bizValueBranch.Count)
            {
                return false;
            }

            var bizValue = bizValueBranch[index];
            var node = options.FirstOrDefault(o => o.Label == bizValue);
            var optionsChanged = false;
            if (node == null)
            {
                node = new MultilevelDataOptions
                {
                    Label = bizValue,
                    Value = Guid.NewGuid().ToString()
                };
                options.Add(node);
                optionsChanged = true;
            }

            if (index < bizValueBranch.Count - 1)
            {
                node.Children = [];
                optionsChanged = optionsChanged || AddBranchOptions(node.Children, bizValueBranch, index + 1);
            }

            return optionsChanged;
        }
    }
}