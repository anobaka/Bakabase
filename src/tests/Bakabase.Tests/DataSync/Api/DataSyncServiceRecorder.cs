using System.Reflection;
using System.Runtime.CompilerServices;
using Bakabase.Modules.DataSync.Services;

namespace Bakabase.Tests.DataSync.Api;

/// <summary>
/// The data sync facade as a recorder, for the controller's tests: every call is recorded with its arguments and
/// answered with <see cref="Answers"/>'s value for that member, else with success — null where the member may answer
/// null (a problem, a missing item), an empty list, or an empty result whose <c>Problem</c> is null.
/// </summary>
public class DataSyncServiceRecorder : DispatchProxy
{
    private static readonly NullabilityInfoContext Nullability = new();

    private readonly List<(string Member, object?[] Args)> _calls = [];

    /// <summary>Answers by member name, instead of success.</summary>
    public Dictionary<string, object?> Answers { get; } = new();

    public IReadOnlyList<(string Member, object?[] Args)> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.._calls];
            }
        }
    }

    public IReadOnlyList<string> Members => Calls.Select(c => c.Member).ToList();

    public static (IDataSyncService Service, DataSyncServiceRecorder Recorder) Create()
    {
        var service = Create<IDataSyncService, DataSyncServiceRecorder>();
        return (service, (DataSyncServiceRecorder) (object) service);
    }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        lock (_calls)
        {
            _calls.Add((method!.Name, args ?? []));
        }

        if (!method.ReturnType.IsGenericType)
        {
            return Task.CompletedTask;
        }

        var type = method.ReturnType.GetGenericArguments().Single();
        var answer = Answers.TryGetValue(method.Name, out var given) ? given
            : Nullability.Create(method.ReturnParameter).GenericTypeArguments[0].ReadState ==
              NullabilityState.Nullable ? null
            : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                ? Array.CreateInstance(type.GetGenericArguments()[0], 0)
                : RuntimeHelpers.GetUninitializedObject(type);
        return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type).Invoke(null, [answer]);
    }
}
