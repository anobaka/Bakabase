using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.TextProcessing;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures.BmMultipleValueProcessor
{
    public abstract class BmCommonMultipleValueProcessor<TProperty> : BmAbstractBmProcessor<
        BmMultipleValueProcessorValue<int, string, TextProcessValue>> where TProperty : notnull
    {
        protected abstract List<TProperty>? GetValues(ResourceDto resource);
        protected abstract void SetValues(ResourceDto resource, List<TProperty>? values);
        protected abstract int GetValueKey(TProperty value);
        protected abstract TProperty CreateNewValue(int id);
        protected abstract string? GetValueBizKey(TProperty value);
        protected abstract TProperty CreateNewValue(string value);

        protected override async Task ProcessInternal(
            BmMultipleValueProcessorValue<int, string, TextProcessValue> value,
            ResourceDto resource, Dictionary<string, string?> variables,
            string? propertyKey)
        {
            switch (value!.Operation)
            {
                case BmMultipleValueProcessorOperation.Add:
                {
                    var newIds = value.SelectedKeys;
                    var currentValues = GetValues(resource);
                    if (newIds != null)
                    {
                        var currentIds = currentValues?.Select(GetValueKey).ToHashSet();
                        if (currentIds != null)
                        {
                            newIds.RemoveAll(currentIds.Contains);
                        }

                        if (newIds.Any())
                        {
                            currentValues ??= [];
                            currentValues.AddRange(newIds.Select(CreateNewValue));
                            SetValues(resource, currentValues);
                        }
                    }

                    if (value.NewData != null)
                    {
                        currentValues ??= [];
                        currentValues.AddRange(value.NewData.Distinct().Select(CreateNewValue));
                    }

                    break;
                }
                case BmMultipleValueProcessorOperation.Remove:
                {
                    SetValues(resource, null);
                    break;
                }
                case BmMultipleValueProcessorOperation.SetWithFixedValue:
                {
                    var targetData = new List<TProperty>();
                    if (value.SelectedKeys != null)
                    {
                        targetData.AddRange(value.SelectedKeys.Distinct().Select(CreateNewValue));
                    }

                    if (value.NewData != null)
                    {
                        targetData.AddRange(value.NewData.Distinct().Select(CreateNewValue));
                    }

                    SetValues(resource, targetData);
                    break;
                }
                case BmMultipleValueProcessorOperation.Modify:
                {
                    var targetData = GetValues(resource);
                    if (targetData?.Any() == true)
                    {
                        switch (value.FilterBy)
                        {
                            case BmMultipleValueProcessorFilterBy.All:
                            {
                                break;
                            }
                            case BmMultipleValueProcessorFilterBy.Containing:
                            {
                                targetData = targetData.Where(o => GetValueBizKey(o)?.Contains(value.Find!) == true)
                                    .ToList();
                                break;
                            }
                            case BmMultipleValueProcessorFilterBy.Matching:
                            {
                                targetData = targetData.Where(o =>
                                    {
                                        var bizKey = GetValueBizKey(o);
                                        return !string.IsNullOrEmpty(bizKey) && Regex.IsMatch(bizKey, value.Find!);
                                    })
                                    .ToList();
                                break;
                            }
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        if (targetData.Any())
                        {
                            Dictionary<TProperty, TProperty?>? changes = null;
                            foreach (var to in targetData!)
                            {
                                var currentBizKey = GetValueBizKey(to);
                                var newName = currentBizKey.Process(value.ChildProcessorValue!)!;
                                if (string.IsNullOrEmpty(newName))
                                {
                                    (changes ??= new Dictionary<TProperty, TProperty?>())[to] = default;
                                }
                                else
                                {
                                    if (currentBizKey != newName)
                                    {
                                        // set as a new data
                                        (changes ??= new Dictionary<TProperty, TProperty?>())[to] =
                                            CreateNewValue(newName);
                                    }
                                }
                            }

                            if (changes != null)
                            {
                                var newData = new List<TProperty>();
                                foreach (var t in targetData)
                                {
                                    if (changes.TryGetValue(t, out var @new))
                                    {
                                        if (@new != null)
                                        {
                                            newData.Add(@new);
                                        }
                                    }
                                    else
                                    {
                                        newData.Add(t);
                                    }
                                }

                                SetValues(resource, newData);
                            }
                        }
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}