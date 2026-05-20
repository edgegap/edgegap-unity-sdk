using System.Collections.Generic;
using System.Linq;

namespace Edgegap.ServerBrowser
{
    using FloatOperator = IntOperator;

    public class FilterCompiler
    {
        public List<FilterBase> Filters = new List<FilterBase>();

        public override string ToString()
        {
            return string.Join(" and ", Filters.Select(f => f.ToString()));
        }
    }

    public abstract class FilterBase
    {
        public abstract override string ToString();
    }

    public abstract class Filter<T> : FilterBase
    {
        public string Field;
        public string Operator;
        public T Value;

        public override string ToString()
        {
            return $"{Field} {Operator} {Value}";
        }
    }

    public class StringFilter : Filter<string>
    {
        public override string ToString()
        {
            return $"{Field} {Operator} '{Value}'";
        }
    }

    public class IntFilter : Filter<int> { }

    public class FloatFilter : Filter<float> { }

    public class BoolFilter : Filter<bool> { }

    public static class StringOperator
    {
        public static readonly string _Equals = "eq";
        public static readonly string _NotEquals = "ne";
        public static readonly string _LessThan = "lt";
        public static readonly string _LessThanOrEqualTo = "le";
        public static readonly string _GreaterThan = "gt";
        public static readonly string _GreaterThanOrEqualTo = "ge";
        public static readonly string _Contains = "contains";
    }

    public static class IntOperator
    {
        public static readonly string _Equals = "eq";
        public static readonly string _NotEquals = "ne";
        public static readonly string _LessThan = "lt";
        public static readonly string _LessThanOrEqualTo = "le";
        public static readonly string _GreaterThan = "gt";
        public static readonly string _GreaterThanOrEqualTo = "ge";
    }

    public static class BoolOperator
    {
        public static readonly string _Equals = "eq";
        public static readonly string _NotEquals = "ne";
    }
}
