using System.Text;

namespace Bakabase.Modules.DataSync.Ordering;

/// <summary>
/// Fractional-index order keys (§3.7): strings that sort ordinally and always leave room between two of them.
/// </summary>
/// <remarks>
/// <para>
/// A key is an integer part followed by a fraction, both written in base-62 digits <c>0-9A-Za-z</c> (ascending
/// in character code, so ordinal string order is numeric order). The integer part starts with a head letter
/// that encodes its length: <c>a</c>…<c>z</c> are the non-negative integers with 1…26 digits, <c>Z</c>…<c>A</c>
/// the negative ones with 1…26 digits. So <c>a0</c> is zero, <c>a1</c> one, <c>Zz</c> minus one, <c>b00</c> the
/// first two-digit integer. The fraction never ends in <c>0</c>, which keeps every value's key unique.
/// </para>
/// <para>
/// Everything is deterministic: equal inputs give equal keys, which is what makes two devices that append at
/// the same time produce equal keys (the tie rule of <see cref="DataSyncOrderPlanner"/>).
/// </para>
/// </remarks>
public static class FractionalIndex
{
    /// <summary>The base-62 digits in ascending character code order.</summary>
    public const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private const char Zero = '0';
    private const char Nine = 'z';

    /// <summary>The smallest integer (<c>A</c> and 26 zeros); it cannot be decremented, so it is not a key.</summary>
    private static readonly string SmallestInteger = "A" + new string(Zero, 26);

    /// <summary>True for a well-formed key. Peer input: never throws.</summary>
    public static bool IsValid(string? key)
    {
        if (string.IsNullOrEmpty(key) || key == SmallestInteger) return false;
        var length = IntegerLength(key[0]);
        if (length == 0 || length > key.Length) return false;
        for (var i = 1; i < key.Length; i++)
        {
            if (DigitOf(key[i]) < 0) return false;
        }

        return key.Length == length || key[^1] != Zero;
    }

    /// <summary>
    /// A key strictly between <paramref name="a"/> and <paramref name="b"/>; null means an open end. Throws
    /// <see cref="ArgumentException"/> for a malformed key and unless <c>a &lt; b</c> (ordinally).
    /// </summary>
    public static string Between(string? a, string? b)
    {
        Validate(a, nameof(a));
        Validate(b, nameof(b));
        if (a is not null && b is not null && string.CompareOrdinal(a, b) >= 0)
            throw new ArgumentException($"Order key '{a}' must sort before '{b}'.", nameof(a));

        if (a is null)
        {
            if (b is null) return "a" + Zero;

            var ib = IntegerPart(b);
            var fb = b[ib.Length..];
            if (ib == SmallestInteger) return ib + Midpoint("", fb);
            if (ib.Length < b.Length) return ib;
            return DecrementInteger(ib) ?? throw new ArgumentException($"No key sorts before '{b}'.", nameof(b));
        }

        if (b is null)
        {
            var ia = IntegerPart(a);
            var fa = a[ia.Length..];
            return IncrementInteger(ia) ?? ia + Midpoint(fa, null);
        }

        {
            var ia = IntegerPart(a);
            var fa = a[ia.Length..];
            var ib = IntegerPart(b);
            var fb = b[ib.Length..];
            if (ia == ib) return ia + Midpoint(fa, fb);

            // a's integer is smaller than b's, so it is never the largest integer.
            var next = IncrementInteger(ia)!;
            return string.CompareOrdinal(next, b) < 0 ? next : ia + Midpoint(fa, null);
        }
    }

    /// <summary>
    /// <paramref name="n"/> distinct keys between <paramref name="a"/> and <paramref name="b"/>, in ascending order.
    /// With an open end they are consecutive integers; between two keys they are spread by halving, so they stay
    /// short. Same preconditions as <see cref="Between"/>.
    /// </summary>
    public static IReadOnlyList<string> NBetween(string? a, string? b, int n)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(n);
        Validate(a, nameof(a));
        Validate(b, nameof(b));
        if (a is not null && b is not null && string.CompareOrdinal(a, b) >= 0)
            throw new ArgumentException($"Order key '{a}' must sort before '{b}'.", nameof(a));

        var result = new List<string>(n);
        AppendBetween(a, b, n, result);
        return result;
    }

    private static void AppendBetween(string? a, string? b, int n, List<string> result)
    {
        if (n == 0) return;
        if (n == 1)
        {
            result.Add(Between(a, b));
            return;
        }

        if (b is null)
        {
            var c = a;
            for (var i = 0; i < n; i++)
            {
                c = Between(c, null);
                result.Add(c);
            }

            return;
        }

        if (a is null)
        {
            var keys = new string[n];
            var c = b;
            for (var i = n - 1; i >= 0; i--)
            {
                c = Between(null, c);
                keys[i] = c;
            }

            result.AddRange(keys);
            return;
        }

        var mid = n / 2;
        var middle = Between(a, b);
        AppendBetween(a, middle, mid, result);
        result.Add(middle);
        AppendBetween(middle, b, n - mid - 1, result);
    }

    /// <summary>
    /// A fraction strictly between fractions <paramref name="a"/> (may be empty) and <paramref name="b"/> (null =
    /// one), both without a trailing zero.
    /// </summary>
    private static string Midpoint(string a, string? b)
    {
        var prefix = new StringBuilder();
        while (true)
        {
            if (b is not null)
            {
                // Drop the longest common prefix, reading a missing digit of a as zero. b cannot run out first,
                // because a < b and neither ends in zero.
                var n = 0;
                while (n < b.Length && (n < a.Length ? a[n] : Zero) == b[n]) n++;
                if (n > 0)
                {
                    prefix.Append(b, 0, n);
                    a = n < a.Length ? a[n..] : "";
                    b = b[n..];
                    continue;
                }
            }

            var digitA = a.Length > 0 ? DigitOf(a[0]) : 0;
            var digitB = b is not null ? DigitOf(b[0]) : Digits.Length;
            if (digitB - digitA > 1)
            {
                // Rounds half up.
                return prefix.Append(Digits[(digitA + digitB + 1) / 2]).ToString();
            }

            // The first digits are consecutive.
            if (b is { Length: > 1 }) return prefix.Append(b[0]).ToString();

            // b is null or one digit: keep a's first digit and go on above the rest of a.
            prefix.Append(Digits[digitA]);
            a = a.Length > 0 ? a[1..] : "";
            b = null;
        }
    }

    private static string? IncrementInteger(string integer)
    {
        var head = integer[0];
        var digits = integer.ToCharArray(1, integer.Length - 1).ToList();
        var carry = true;
        for (var i = digits.Count - 1; carry && i >= 0; i--)
        {
            var d = DigitOf(digits[i]) + 1;
            if (d == Digits.Length)
            {
                digits[i] = Zero;
            }
            else
            {
                digits[i] = Digits[d];
                carry = false;
            }
        }

        if (!carry) return head + new string(digits.ToArray());
        if (head == 'Z') return "a" + Zero;
        if (head == 'z') return null;

        var nextHead = (char)(head + 1);
        if (nextHead > 'a') digits.Add(Zero);
        else digits.RemoveAt(digits.Count - 1);
        return nextHead + new string(digits.ToArray());
    }

    private static string? DecrementInteger(string integer)
    {
        var head = integer[0];
        var digits = integer.ToCharArray(1, integer.Length - 1).ToList();
        var borrow = true;
        for (var i = digits.Count - 1; borrow && i >= 0; i--)
        {
            var d = DigitOf(digits[i]) - 1;
            if (d == -1)
            {
                digits[i] = Nine;
            }
            else
            {
                digits[i] = Digits[d];
                borrow = false;
            }
        }

        if (!borrow) return head + new string(digits.ToArray());
        if (head == 'a') return "Z" + Nine;
        if (head == 'A') return null;

        var previousHead = (char)(head - 1);
        if (previousHead < 'Z') digits.Add(Nine);
        else digits.RemoveAt(digits.Count - 1);
        return previousHead + new string(digits.ToArray());
    }

    private static string IntegerPart(string key) => key[..IntegerLength(key[0])];

    /// <summary>Length of the integer part (head letter included) for a head letter; 0 for anything else.</summary>
    private static int IntegerLength(char head) => head switch
    {
        >= 'a' and <= 'z' => head - 'a' + 2,
        >= 'A' and <= 'Z' => 'Z' - head + 2,
        _ => 0,
    };

    private static int DigitOf(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'Z' => c - 'A' + 10,
        >= 'a' and <= 'z' => c - 'a' + 36,
        _ => -1,
    };

    private static void Validate(string? key, string paramName)
    {
        if (key is not null && !IsValid(key))
            throw new ArgumentException($"Invalid order key '{key}'.", paramName);
    }
}
