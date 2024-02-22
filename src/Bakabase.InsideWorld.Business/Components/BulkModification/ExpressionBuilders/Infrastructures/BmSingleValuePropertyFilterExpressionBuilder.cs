using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.ExpressionBuilders.Infrastructures
{
    /// <summary>
    /// 
    /// </summary>
    public abstract class BmSingleValuePropertyFilterExpressionBuilder<TValue> : BmAbstractFilterExpressionBuilder
    {
        protected abstract TValue? GetValue(ResourceDto resource);
        protected virtual string? ToString(TValue? value) => value?.ToString();
        private static bool IsComparable => typeof(IComparable).IsAssignableFrom(typeof(TValue));

        protected override Expression<Func<ResourceDto, bool>> BuildInternal(BulkModificationFilter filter)
        {
            Func<ResourceDto, bool> func;

            switch (filter.Operation)
            {
                case BulkModificationFilterOperation.Equals:
                case BulkModificationFilterOperation.NotEquals:
                {
                    var target = JsonConvert.DeserializeObject<TValue>(filter.Target!)!;
                    func = filter.Operation switch
                    {
                        BulkModificationFilterOperation.Equals => BuildEquals(target),
                        BulkModificationFilterOperation.NotEquals => r => GetValue(r)?.Equals(target) != true,
                        _ => throw new ArgumentOutOfRangeException()
                    };
                    break;
                }
                case BulkModificationFilterOperation.Contains:
                case BulkModificationFilterOperation.NotContains:
                case BulkModificationFilterOperation.StartsWith:
                case BulkModificationFilterOperation.NotStartsWith:
                case BulkModificationFilterOperation.EndsWith:
                case BulkModificationFilterOperation.NotEndsWith:
                case BulkModificationFilterOperation.Matches:
                case BulkModificationFilterOperation.NotMatches:
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
                                ToString(GetValue(r))?.StartsWith(target) == true,
                            BulkModificationFilterOperation.NotStartsWith => r =>
                                ToString(GetValue(r))?.StartsWith(target) != true,
                            BulkModificationFilterOperation.EndsWith => r =>
                                ToString(GetValue(r))?.EndsWith(target) == true,
                            BulkModificationFilterOperation.NotEndsWith => r =>
                                ToString(GetValue(r))?.EndsWith(target) != true,
                            BulkModificationFilterOperation.Matches => r =>
                            {
                                var v = ToString(GetValue(r));
                                return !string.IsNullOrEmpty(v) && Regex.IsMatch(v, target);
                            },
                            BulkModificationFilterOperation.NotMatches => r =>
                            {
                                var v = ToString(GetValue(r));
                                return string.IsNullOrEmpty(v) || !Regex.IsMatch(v, target);
                            },
                            BulkModificationFilterOperation.Contains => r =>
                                ToString(GetValue(r))?.Contains(target) == true,
                            BulkModificationFilterOperation.NotContains => r =>
                                ToString(GetValue(r))?.Contains(target) != true,
                            _ => throw new ArgumentOutOfRangeException()
                        };
                    }

                    break;
                }
                case BulkModificationFilterOperation.IsNull:
                {
                    func = r => GetValue(r) == null;
                    break;
                }
                case BulkModificationFilterOperation.IsNotNull:
                {
                    func = r => GetValue(r) != null;
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
                                var v = GetValue(r);
                                return v != null && target.Contains(v);
                            },
                            BulkModificationFilterOperation.NotIn => r =>
                            {
                                var v = GetValue(r);
                                return v == null || target.Contains(v);
                            },
                            _ => throw new ArgumentOutOfRangeException()
                        };
                    }

                    break;
                }

                case BulkModificationFilterOperation.GreaterThan:
                case BulkModificationFilterOperation.LessThan:
                case BulkModificationFilterOperation.GreaterThanOrEquals:
                case BulkModificationFilterOperation.LessThanOrEquals:
                {
                    if (string.IsNullOrEmpty(filter.Target) || !IsComparable)
                    {
                        func = r => true;
                    }
                    else
                    {
                        var target = JsonConvert.DeserializeObject<TValue>(filter.Target!)!;
                        func = filter.Operation switch
                        {
                            BulkModificationFilterOperation.GreaterThan => r =>
                            {
                                var v = GetValue(r);
                                return v != null && ((IComparable) v).CompareTo(target) < 0;
                            },
                            BulkModificationFilterOperation.LessThan => r =>
                            {
                                var v = GetValue(r);
                                return v != null && ((IComparable) v).CompareTo(target) > 0;
                            },
                            BulkModificationFilterOperation.GreaterThanOrEquals => r =>
                            {
                                var v = GetValue(r);
                                return v != null && ((IComparable) v).CompareTo(target) <= 0;
                            },
                            BulkModificationFilterOperation.LessThanOrEquals => r =>
                            {
                                var v = GetValue(r);
                                return v != null && ((IComparable) v).CompareTo(target) >= 0;
                            },
                            _ => throw new ArgumentOutOfRangeException()
                        };

                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return r => func(r);
        }

        protected virtual Func<ResourceDto, bool> BuildEqualsOrNotEquals(BulkModificationFilter filter)
        {
            var target = JsonConvert.DeserializeObject<TValue>(filter.Target!)!;
            Func<ResourceDto, bool> func = filter.Operation switch
            {
                BulkModificationFilterOperation.Equals => r => GetValue(r)?.Equals(target) == true,
                BulkModificationFilterOperation.NotEquals => r => GetValue(r)?.Equals(target) != true,
                _ => throw new ArgumentOutOfRangeException()
            };
            return func;
        }

        protected override Func<ResourceDto, bool> BuildEquals(BulkModificationFilter filter) =>
            BuildEqualsOrNotEquals(filter);

        protected override Func<ResourceDto, bool> BuildContains(BulkModificationFilter filter) =>
            BuildEqualsOrNotEquals(filter);

        protected override Func<ResourceDto, bool> BuildEndsWith(BulkModificationFilter filter)
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