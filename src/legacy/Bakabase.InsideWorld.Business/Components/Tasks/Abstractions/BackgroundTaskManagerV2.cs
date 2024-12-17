//using System;
//using System.Threading.Tasks;
//using Bakabase.InsideWorld.Models.RequestModels;

//namespace Bakabase.InsideWorld.Business.Components.Tasks.Abstractions;

//public class BackgroundTaskManagerV2
//{
//    public async Task Daemon()
//    {

//    }

//    public async Task PendTask<TDescriptor>() where TDescriptor: IBackgroundTaskDescriptor
//    {
//        IBackgroundTaskDescriptor descriptor;
//        if (descriptor.ArgType != null)
//        {
//            throw new ArgumentException("Invalid argument type");
//        }

//        // save
//    }

//    public async Task PendTask(IBackgroundTaskArgs args)
//    {
//        IBackgroundTaskDescriptor descriptor;
//        if (descriptor.ArgType != args.GetType())
//        {
//            throw new ArgumentException("Invalid argument type");
//        }

//        // save 
//    }

//    public async Task Test()
//    {

//        await PendTask(new ResourceMoveIBackgroundTaskArgs());
//    }
//}

//public record ResourceMoveIBackgroundTaskArgs : IBackgroundTaskArgs
//{

//}

//public class MoveResourceTaskDescriptor : AbstractBackgroundTaskDescriptor<ResourceMoveRequestModel>
//{
//    public override string Id => "MoveResourceTask";
//    public override string Name { get; set; }
//}