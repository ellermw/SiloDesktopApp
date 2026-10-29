using System.Globalization;
using System.Text.RegularExpressions;

namespace SiloPlayer.UpdateHost;

internal sealed record HostOptions(bool Worker,string AppPipe,string WorkerPipe,int ParentPid,long ParentStart,
    string PackageDirectory,string Tag,bool Restart,long Owner,int BrokerPid,long BrokerStart)
{
    public static HostOptions Parse(string[] args)
    {
        if(args.Length is < 16 or > 24 || args.Length%2!=0)throw new InvalidDataException();
        var values=new Dictionary<string,string>(StringComparer.Ordinal);
        for(var i=0;i<args.Length;i+=2)if(!values.TryAdd(args[i],args[i+1]))throw new InvalidDataException();
        string Get(string key)=>values.TryGetValue(key,out var value)?value:throw new InvalidDataException();
        foreach(var key in values.Keys)if(key is not ("--worker" or "--app-pipe" or "--worker-pipe" or "--parent" or "--parent-start" or "--package" or "--tag" or "--restart" or "--owner" or "--broker" or "--broker-start"))throw new InvalidDataException();
        foreach(var pipe in new[]{Get("--app-pipe"),Get("--worker-pipe")})if(!Regex.IsMatch(pipe,@"^SiloUpdate-[a-f0-9]{32}$"))throw new InvalidDataException();
        var tag=Get("--tag");if(!Regex.IsMatch(tag,@"^v?\d+\.\d+\.\d+(?:\.\d+)?$"))throw new InvalidDataException();
        return new(values.GetValueOrDefault("--worker")=="yes",Get("--app-pipe"),Get("--worker-pipe"),int.Parse(Get("--parent"),CultureInfo.InvariantCulture),
            long.Parse(Get("--parent-start"),CultureInfo.InvariantCulture),Path.GetFullPath(Get("--package")),tag,Get("--restart")=="yes",
            long.Parse(Get("--owner"),CultureInfo.InvariantCulture),int.Parse(values.GetValueOrDefault("--broker","0"),CultureInfo.InvariantCulture),
            long.Parse(values.GetValueOrDefault("--broker-start","0"),CultureInfo.InvariantCulture));
    }
    public IEnumerable<string> Arguments(bool worker,int brokerPid,long brokerStart)
    {
        return new[]{"--worker",worker?"yes":"no","--app-pipe",AppPipe,"--worker-pipe",WorkerPipe,"--parent",ParentPid.ToString(CultureInfo.InvariantCulture),
            "--parent-start",ParentStart.ToString(CultureInfo.InvariantCulture),"--package",PackageDirectory,"--tag",Tag,"--restart",Restart?"yes":"no",
            "--owner",Owner.ToString(CultureInfo.InvariantCulture),"--broker",brokerPid.ToString(CultureInfo.InvariantCulture),"--broker-start",brokerStart.ToString(CultureInfo.InvariantCulture)};
    }
}
