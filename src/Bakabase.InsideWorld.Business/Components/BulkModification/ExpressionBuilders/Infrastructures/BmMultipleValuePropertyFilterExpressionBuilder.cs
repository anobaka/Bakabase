using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders.Infrastructures
{
    public abstract class BmMultipleValuePropertyFilterExpressionBuilder<TValue> : BmAbstractFilterExpressionBuilder
    {
        protected abstract HashSet<TValue>? GetValue(ResourceDto resource, BulkModificationFilter filter);
        protected virtual string? ToString(TValue? value) => value?.ToString();

        protected override Expression<Func<ResourceDto, bool>> BuildInternal(BulkModificationFilter filter)
        {
            Func<ResourceDto, bool> func;

            switch (filter.Operation)
            {
                case BulkModificationFilterOperation.Equals:
                case BulkModificationFilterOperation.NotEquals:
                {
                    if (string.IsNullOrEmpty(filter.Target))
                    {
                        func = r => true;
                    }
                    else
                    {
                        var target = JsonConvert.DeserializeObject<HashSet<TValue>>(filter.Target!)!;
                        func = filter.Operation switch
                        {
                            BulkModificationFilterOperation.Equals => r =>
                            {
                                var v = GetValue(r, filter);
                                return v != null && v.Count == target.Count && v.All(x => target.Contains(x));
                            },
                            BulkModificationFilterOperation.NotEquals => r =>
                            {
                                var v = GetValue(r, filter);
                                return v == null || v.Count == target.Count || v.Any(x => !target.Contains(x));
                            },
                            _ => throw new ArgumentOutOfRangeException()
                        };
                    }

                    break;
                }
                case BulkModificationFilterOperation.IsNull:
                {
                    func = r => GetValue(r, filter)?.Any() != true;
                    break;
                }
                case BulkModificationFilterOperation.IsNotNull:
                {
                    func = r => GetValue(r, filter)?.Any() == true;
                    break;
                }
                case BulkModificationFilterOperation.In:
                case BulkModificationFilterOperation.NotIn:
                {
                    if (string.IsNullOrEmpty(filter.Target))
                    {
                        func = r => true;
                    }
                    else
                    {
                        var target = JsonConvert.DeserializeObject<HashSet<TValue>>(filter.Target!)!;
                        func = filter.Operation switch
                        {
                            BulkModificationFilterOperation.In => r =>
                            {
                                var v = GetValue(r, filter);
                                return v?.All(x => target.Contains(x)) == true;
                            },
                            BulkModificationFilterOperation.NotIn => r =>
                            {
                                var v = GetValue(r, filter);
                                return v?.All(x => target.Contains(x)) != true;
                            },
                            _ => throw new ArgumentOutOfRangeException()
                        };
                    }

                    break;
                }
                case BulkModificationFilterOperation.StartsWith:
                case BulkModificationFilterOperation.NotStartsWith:
                case BulkModificationFilterOperation.EndsWith:
                case BulkModificationFilterOperation.NotEndsWith:
                case BulkModificationFilterOperation.Matches:
                case BulkModificationFilterOperation.NotMatches:
                case BulkModificationFilterOperation.Contains:
                case BulkModificationFilterOperation.NotContains:
                {
                    if (string.IsNullOrEmpty(filter.Target))
                    {
                        func = r => true;
                    }
                    else
                    {
                        var target = JsonConvert.DeserializeObject<string>(filter.Target!)!;
                        func = filter.Operation switch
                        {
                            BulkModificationFilterOperation.StartsWith => r =>
                                GetValue(r, filter)?.Any(x => ToString(x)?.StartsWith(target) == true) == true,
                            BulkModificationFilterOperation.NotStartsWith => r =>
                                GetValue(r, filter)?.Any(x => ToString(x)?.StartsWith(target) == true) != true,
                            BulkModificationFilterOperation.EndsWith => r =>
                                GetValue(r, filter)?.Any(x => ToString(x)?.EndsWith(target) == true) == true,
                            BulkModificationFilterOperation.NotEndsWith => r =>
                                GetValue(r, filter)?.Any(x => ToString(x)?.EndsWith(target) == true) != true,
                            BulkModificationFilterOperation.Matches => r => GetValue(r, filter)?.Any(x =>
                            {
                                var s = ToString(x);
                                return !string.IsNullOrEmpty(s) && Regex.IsMatch(s, target);
                            }) == true,
                            BulkModificationFilterOperation.NotMatches => r => GetValue(r, filter)?.Any(x =>
                            {
                                var s = ToString(x);
                                return !string.IsNullOrEmpty(s) && Regex.IsMatch(s, target);
                            }) != true,
                            BulkModificationFilterOperation.Contains => r =>
                                GetValue(r, filter)?.Select(ToString).Contains(target) == true,
                            BulkModificationFilterOperation.NotContains => r =>
                                GetValue(r, filter)?.Select(ToString).Contains(target) != true,
                            _ => throw new ArgumentOutOfRangeException()
                        };
                    }

                    break;
                }
                case BulkModificationFilterOperation.GreaterThan:
                case BulkModificationFilterOperation.LessThan:
                case BulkModificationFilterOperation.GreaterThanOrEquals:
                case BulkModificationFilterOperation.LessThanOrEquals:
                // {
                //     if (string.IsNullOrEmpty(filter.Target) || !IsComparable)
                //     {
                //         func = r => true;
                //     }
                //     else
                //     {
                //         var target = JsonConvert.DeserializeObject<TValue>(filter.Target!)!;
                //         func = filter.Operation switch
                //         {
                //             BulkModificationFilterOperation.GreaterThan => r =>
                //             {
                //                 var v = GetValue(r, filter);
                //                 return v != null && ((IComparable) v).CompareTo(target) < 0;
                //             },
                //             BulkModificationFilterOperation.LessThan => r =>
                //             {
                //                 var v = GetValue(r, filter);
                //                 return v != null && ((IComparable) v).CompareTo(target) > 0;
                //             },
                //             BulkModificationFilterOperation.GreaterThanOrEquals => r =>
                //             {
                //                 var v = GetValue(r, filter);
                //                 return v != null && ((IComparable) v).CompareTo(target) <= 0;
                //             },
                //             BulkModificationFilterOperation.LessThanOrEquals => r =>
                //             {
                //                 var v = GetValue(r, filter);
                //                 return v != null && ((IComparable) v).CompareTo(target) >= 0;
                //             },
                //             _ => throw new ArgumentOutOfRangeException()
                //         };
                //
                //     }
                //
                //     break;
                // }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return r => func(r);
        }

        protected override Func<ResourceDto, bool> BuildContains(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildEndsWith(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildEquals(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildGreaterThan(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildGreaterThanOrEquals(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildIn(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildIsNotNull(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildIsNull(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildLessThan(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildLessThanOrEquals(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildMatches(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildNotContains(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildNotEndsWith(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildNotEquals(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildNotIn(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildNotMatches(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildNotStartsWith(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }

        protected override Func<ResourceDto, bool> BuildStartsWith(BulkModificationFilter filter)
        {
            throw new NotImplementedException();
        }
    }
}