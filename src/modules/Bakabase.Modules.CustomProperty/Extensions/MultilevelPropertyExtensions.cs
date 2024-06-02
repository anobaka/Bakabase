using Bakabase.Modules.CustomProperty.Properties.Multilevel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.Modules.CustomProperty.Extensions
{
    public static class MultilevelPropertyExtensions
    {
        public static string[]? FindLabel(this MultilevelDataOptions options, string id)
        {
            if (options.Value == id)
            {
                return [options.Label];
            }

            return options.Children?.Select(child => child.FindLabel(id)).OfType<string[]>()
                .Select(result => new[] {options.Label}.Concat(result).ToArray()).FirstOrDefault();
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