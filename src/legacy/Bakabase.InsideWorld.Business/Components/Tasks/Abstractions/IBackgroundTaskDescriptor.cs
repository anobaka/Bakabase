using System;

namespace Bakabase.InsideWorld.Business.Components.Tasks.Abstractions;

public interface IBackgroundTaskDescriptor
{
    string Id { get; }
    string Name { get; set; }
    Type? ArgType { get; }
}

public interface IBackgroundTaskDescriptor<TData>: IBackgroundTaskDescriptor
{

}

public abstract class AbstractBackgroundTaskDescriptor<TArg> : IBackgroundTaskDescriptor
{
    public abstract string Id { get; }
    public abstract string Name { get; set; }

    public Type ArgType => typeof(TArg);
}