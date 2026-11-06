using GapActivator.Service;
using GapActivator.Service.Setup;

return SetupCommand.IsSetup(args)
    ? await SetupCommand.RunAsync(args[1..], Console.Out, Console.Error)
    : await ServiceEntry.RunAsync(args, FatalEventLog.Write);
