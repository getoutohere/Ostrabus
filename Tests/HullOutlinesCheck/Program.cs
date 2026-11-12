using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ostrabus.Development;
using Ostrabus.NavConsole.Outlines;
using Ostranauts.Core.Models;
using Ostranauts.ShipGUIs.Utilities;
using Ostranauts.Trading;
using UnityEngine;
using Vectrosity;

static partial class Program {
    static int checks;
    static int assignments;
    static readonly Dictionary<CondOwner,HashSet<string>> conditions=new Dictionary<CondOwner,HashSet<string>>();
    static readonly Dictionary<CondOwner,double> installed=new Dictionary<CondOwner,double>();
    static readonly Dictionary<Transform,Vector3> positions=new Dictionary<Transform,Vector3>();
    static readonly Dictionary<GameObject,Transform> transforms=new Dictionary<GameObject,Transform>();
    static void Check(bool ok,string reason){checks++;if(!ok)throw new Exception(reason);}
    static T Empty<T>()=>(T)FormatterServices.GetUninitializedObject(typeof(T));
    static void Field(object target,string field,object value)=>AccessTools.Field(target.GetType(),field).SetValue(target,value);
    static bool Equal(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result){__result=ReferenceEquals(__0,__1);return false;}
    static bool Unequal(UnityEngine.Object __0,UnityEngine.Object __1,ref bool __result){__result=!ReferenceEquals(__0,__1);return false;}
    static bool Has(CondOwner __instance,string strName,ref bool __result){__result=conditions.TryGetValue(__instance,out var values)&&values.Contains(strName);return false;}
    static bool Amount(CondOwner __instance,string strName,ref double __result) {
        __result=strName=="IsInstalled"&&installed.TryGetValue(__instance,out double value)?value:conditions.TryGetValue(__instance,out var values)&&values.Contains(strName)?1:0;
        return false;
    }
    static Vector3 Position(Transform transform)=>positions[transform];
    static Transform TransformOf(GameObject gameObject)=>transforms[gameObject];
    static void AssignBoundary(){assignments++;}
    static bool Skip() => false;
    static IEnumerable<CodeInstruction> EngineBoundary(IEnumerable<CodeInstruction> instructions) {
        foreach(var instruction in instructions) {
            string method=null;
            if(instruction.Calls(AccessTools.PropertyGetter(typeof(Transform),nameof(Transform.position))))method=nameof(Position);
            if(instruction.Calls(AccessTools.PropertyGetter(typeof(GameObject),nameof(GameObject.transform))))method=nameof(TransformOf);
            if(method!=null){instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(Program),method);}
            yield return instruction;
        }
    }
    sealed class Manual {
        internal WaitCallback Callback;internal object State;
        internal void Queue(WaitCallback callback,object state){Check(Callback==null,"More than one snapshot worker");Callback=callback;State=state;}
        internal void Drain(){var callback=Callback;Callback=null;callback(State);State=null;}
    }
    static Ship Hull(Ship.Loaded load,params JsonItem[] items) {
        var ship=Empty<Ship>();ship.strRegID="OFFLINE-HULL";ship.json=new JsonShip{aItems=items};Field(ship,"nLoadState",load);return ship;
    }
    static Envelope Take(Ship ship,out int steps) {
        var manual=new Manual();var worker=new HullWorker(manual.Queue);var entry=worker.Create();
        Check(worker.TryReserve(entry,out var ticket),"Snapshot reservation failed");steps=1;
        using(var snapshot=new HullSnapshot(ship,worker,ticket,256)) {
            while(!snapshot.Step(ship,1000)){steps++;Check(steps<1000,"Snapshot made no progress");}
            Check(snapshot.MissingDefinitions==0,"Snapshot encountered unresolved part definitions");
            snapshot.Submit();
            Check(!snapshot.Owner.TryGetTarget(out _),"Submitted snapshot kept its ship reference");
            Check(AccessTools.Field(typeof(HullSnapshot),"live").GetValue(snapshot)==null&&AccessTools.Field(typeof(HullSnapshot),"saved").GetValue(snapshot)==null,"Submitted snapshot retained game arrays");
        }
        manual.Drain();Check(worker.Read(entry,out var result,out _,out bool clean,out var error)&&clean,"Snapshot result unavailable: "+error);
        worker.Reset(true);return result;
    }
    static DataCO Definition(string name,int columns,string[] sockets,params string[] extra) {
        var conditions=new[]{"IsInstalled=1.0x1","IsCategoryHull=1.0x1"}.Concat(extra).ToArray();
        var definition=new DataCO(new JsonCondOwner{strName=name,aStartingConds=conditions});
        definition.JItmDef=new JsonItemDef{nCols=columns,aSocketAdds=sockets};return definition;
    }
    static void SnapshotCases() {
        DataHandler.dictDataCOs=new Dictionary<string,DataCO>{
            ["floor"]=Definition("floor",1,new[]{"occupied"}),
            ["wide"]=Definition("wide",2,new[]{"occupied","Blank"}),
            ["placeholder"]=Definition("placeholder",1,new[]{"occupied"},"IsPlaceholder=1.0x1")
        };
        var items=new List<JsonItem>();
        for(int x=0;x<20;x++)for(int y=0;y<10;y++)items.Add(new JsonItem{strName="floor",fX=x,fY=y});
        var loose=new JsonItem{strName="floor",fX=999,fY=999};loose.SetCondAmount("IsInstalled",0);items.Add(loose);
        items.Add(new JsonItem{strName="floor",fX=999,fY=999,strParentID="container"});
        items.Add(new JsonItem{strName="placeholder",fX=999,fY=999});
        var original=Take(Hull(Ship.Loaded.Shallow,items.ToArray()),out _);
        Check(original.Count==4&&original.Contains(-.5,-.5,false)&&original.Contains(19.5,9.5,false)&&!original.Contains(30,0,false),"Installed footprint filtering/bounds failed");
        var noInterior=items.Where(p=>p.fX!=10||p.fY!=5).ToArray();
        Check(original.SameDrawing(Take(Hull(Ship.Loaded.Shallow,noInterior),out _)),"Interior salvage changed exterior drawing");
        foreach(int rotation in new[]{0,90,180,270,-90}) {
            var envelope=Take(Hull(Ship.Loaded.Shallow,new JsonItem{strName="wide",fRotation=rotation}),out _);
            HullSnapshot.Rotation(rotation,out double cos,out double sin);
            Check(envelope.Contains(-.5*cos,-.5*sin,false),"Rotated authored occupied socket missing");
            Check(!envelope.Contains(2*cos,2*sin,false),"Blank socket/padding became hull");
        }
        var many=Enumerable.Range(0,5000).Select(i=>new JsonItem{strName="floor",fX=i%100,fY=i/100}).ToArray();
        var large=Take(Hull(Ship.Loaded.Shallow,many),out int steps);
        Check(steps>1&&large.Count==4,"Snapshot work was not spread across bounded steps");
        var cancellation=new HullWorker((_,__)=>throw new Exception("Cancelled snapshot queued work"));var cancelledEntry=cancellation.Create();
        cancellation.TryReserve(cancelledEntry,out var cancelledTicket);
        var aborted=new HullSnapshot(Hull(Ship.Loaded.Shallow,many),cancellation,cancelledTicket,256);
        aborted.Step(Hull(Ship.Loaded.Shallow,many),1000);aborted.Dispose();
        Check(!aborted.Owner.TryGetTarget(out _)&&!cancellation.Valid(cancelledTicket),"Cancelled capture retained owner/reservation");cancellation.Reset(true);

        // Materialized path uses current rotated sockets and root-relative
        // positions. Stale FloorPlan and mutable inventory contents are ignored.
        var live=Hull(Ship.Loaded.Full);live.gameObject=Empty<GameObject>();
        var root=Empty<Transform>();transforms[live.gameObject]=root;positions[root]=new Vector3(1000,2000,0);
        var map=new DictionaryArray<string,CondOwner>();Field(live,"mapICOs",map);
        int serial=0;
        foreach(var p in Enumerable.Range(0,66).Select(i=>new Vector2(i%11,i/11))) {
            var co=Empty<CondOwner>();co.tf=Empty<Transform>();positions[co.tf]=new Vector3(1000+p.x,2000+p.y,0);
            conditions[co]=new HashSet<string>{"IsInstalled","IsCategoryHull"};
            var item=Empty<Item>();item.nWidthInTiles=1;item.nHeightInTiles=1;item.aSocketAdds=new List<Loot>{new Loot{strName="occupied"}};
            Field(co,"_itemComponentReference",item);Field(co,"bTriedGettingItemRef",true);map.Add((serial++).ToString(),co);
        }
        var looseLive=Empty<CondOwner>();looseLive.tf=Empty<Transform>();positions[looseLive.tf]=new Vector3(99999,99999,0);
        conditions[looseLive]=new HashSet<string>{"IsInstalled","IsCategoryHull"};installed[looseLive]=0;map.Add("zero-installed",looseLive);
        live.FloorPlan=new List<Vector2>{new Vector2(99999,99999)};
        var actual=Take(live,out _);
        Check(actual.Count==4&&actual.Contains(-.5,-.5,false)&&actual.Contains(10.5,5.5,false)&&!actual.Contains(1000,2000,false),"Live capture used stale geometry or world-space offsets");
        RendererCases(live,actual);
        // Even when the native dictionary changes its array cache, an in-progress
        // capture owns its original reference snapshot until revision validation.
        var queue=new Manual();var service=new HullWorker(queue.Queue);var entry=service.Create();service.TryReserve(entry,out var ticket);
        using(var snapshot=new HullSnapshot(live,service,ticket,256)) {
            map.Clear();service.Dirty(entry);
            Check(!service.Valid(ticket),"Structural mutation did not invalidate partial snapshot");
        }
        service.Reset(true);
        var expiryService=new HullWorker();var expiryEntry=expiryService.Create();expiryService.TryReserve(expiryEntry,out var expiryTicket);
        var expiring=CreateExpiredArray(live,expiryService,expiryTicket);
        for(int attempt=0;attempt<3;attempt++){GC.Collect();GC.WaitForPendingFinalizers();}
        bool expired=false;try{expiring.Step(live,1000);}catch(SnapshotExpiredException){expired=true;}
        Check(expired,"Partial snapshot strongly retained a replaced native CO array");
        expiring.Dispose();Check(!expiryService.Valid(expiryTicket),"Expired snapshot leaked a reservation");expiryService.Reset(true);GC.KeepAlive(live);
        foreach(int rotation in new[]{0,90,180,270}) {
            var list=new List<Vector2>();HullDisplay.Copy(actual,250,rotation,list);
            Check(list.Count==actual.Count*2&&list[0]==list[list.Count-1],"Rendered exterior edges not closed");
            Check(Math.Abs(list.Max(p=>p.magnitude)-250)<.001,"Renderer changed native radius normalization");
            HullSnapshot.Rotation(-rotation,out double cos,out double sin);var first=actual.Unit(0);
            Check(Math.Abs(list[0].x-(first.X*cos-first.Y*sin)*250)<.001&&Math.Abs(list[0].y-(first.X*sin+first.Y*cos)*250)<.001,"Renderer changed native grid compensation");
            list.Clear();Check(actual.Count==4,"Mutable renderer list corrupted shared envelope");
        }
        var fragments=new EnvelopeBuilder().Build(new[]{new HullPoint(0,0),new HullPoint(1,0),new HullPoint(1,1),new HullPoint(0,1),
            new HullPoint(10,0),new HullPoint(11,0),new HullPoint(11,1),new HullPoint(10,1)},8,null);
        var edges=new List<Vector2>();HullDisplay.Copy(fragments,250,0,edges);
        Check(edges.Count==16&&fragments.Loops==2,"Detached component rendering is incomplete");
        for(int i=0;i<edges.Count;i+=2)Check((edges[i]-edges[i+1]).magnitude<100,"Renderer bridged separate hull fragments");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static HullSnapshot CreateExpiredArray(Ship ship,HullWorker worker,HullWorker.Ticket ticket) {
        var map=new DictionaryArray<string,CondOwner>();map.Add("temporary",Empty<CondOwner>());Field(ship,"mapICOs",map);
        var snapshot=new HullSnapshot(ship,worker,ticket,256);map.Clear();return snapshot;
    }
    static void WaitReady(HullWorker worker,HullWorker.Entry entry) {
        var timer=System.Diagnostics.Stopwatch.StartNew();
        while(!worker.Read(entry,out _,out _,out bool clean,out _)||!clean) {
            if(timer.ElapsedMilliseconds>5000)throw new Exception("Production display worker did not publish a result");
            Thread.Sleep(1);
        }
    }
    static void Publish(HullWorker worker,HullWorker.Entry entry,Envelope geometry) {
        Check(worker.TryReserve(entry,out var ticket),"Production display request could not be reserved");
        var buffer=worker.Rent(geometry.Count);for(int i=0;i<geometry.Count;i++)buffer[i]=geometry.Raw(i);
        worker.Submit(ticket,buffer,geometry.Count);WaitReady(worker,entry);
    }
    static void RendererCases(Ship ship,Envelope geometry) {
        var worker=(HullWorker)AccessTools.Field(typeof(HullDisplay),"worker").GetValue(null);
        var source=AccessTools.Method(typeof(HullDisplay),"Create").Invoke(null,new object[]{ship});
        var entry=(HullWorker.Entry)AccessTools.Field(source.GetType(),"Entry").GetValue(source);
        var table=AccessTools.Field(typeof(HullDisplay),"sources").GetValue(null);table.GetType().GetMethod("Add").Invoke(table,new[]{(object)ship,source});
        var situ=Empty<ShipSitu>();situ.Size=250;Field(ship,"<objSS>k__BackingField",situ);
        var line=NativeLine(5);
        var draw=new ShipDraw(ship){lineBodySilhouette=line,silhouetteDrawPoints=line.points2,SignalVisibility=Ostranauts.Ships.Sensors.SignalVisibility.Visible};
        var initial=Ostranauts.ShipGUIs.NavStation.NavIcon.GetSilhouette(ship,250);
        Check(initial.Count>0&&ship.SilhouettePoints==null,"Initial icon invoked the defective native tracer");
        Publish(worker,entry,geometry);
        assignments=0;draw.ToggleSilhouetteDrawMode(true);draw.ToggleSilhouetteDrawMode(true);
        Check(assignments==1,"Unchanged envelope reuploaded on every draw");
        Check(draw.lineBodySilhouette.lineType==LineType.Discrete,"Actual renderer adoption kept continuous line mode");
        SyncLine(draw.lineBodySilhouette);
        var drawn=draw.silhouetteDrawPoints.ToArray();
        var readOnly=Ostranauts.ShipGUIs.NavStation.NavIcon.GetSilhouette(ship,250);readOnly.Clear();
        Check(draw.silhouetteDrawPoints.SequenceEqual(drawn)&&geometry.Count==4,"Initial/projection icons shared a mutable list with an existing renderer");
        draw.ToggleSilhouetteDrawMode(false);draw.SignalVisibility=Ostranauts.Ships.Sensors.SignalVisibility.Partial;draw.ToggleSilhouetteDrawMode(true);
        Check(assignments==1,"Hidden/symbolic contact received a detailed hull upload");draw.SignalVisibility=Ostranauts.Ships.Sensors.SignalVisibility.Visible;
        var interior=Empty<CondOwner>();interior.tf=Empty<Transform>();positions[interior.tf]=new Vector3(1005,2002,0);
        conditions[interior]=new HashSet<string>{"IsInstalled","IsCategoryHull"};var item=Empty<Item>();item.nWidthInTiles=1;item.nHeightInTiles=1;
        Field(interior,"_itemComponentReference",item);Field(interior,"bTriedGettingItemRef",true);
        var mutate=AccessTools.Method(typeof(Ship),"UpdateTiles");
        mutate.Invoke(ship,new object[]{interior,true,false});
        worker.Read(entry,out _,out _,out bool clean,out _);Check(clean,"Strict interior salvage dirtied the envelope");
        var map=(DictionaryArray<string,CondOwner>)AccessTools.Field(typeof(Ship),"mapICOs").GetValue(ship);
        var boundary=map.Values.First();conditions[boundary].Remove("IsInstalled"); // Uninstall may clear this before removing tile contributions.
        mutate.Invoke(ship,new object[]{boundary,true,false});
        worker.Read(entry,out _,out _,out clean,out _);Check(!clean,"Boundary salvage did not dirty the envelope");
        Field(source,"NextCapture",double.MaxValue);draw.ToggleSilhouetteDrawMode(true);
        Check(assignments==1&&draw.silhouetteDrawPoints.SequenceEqual(drawn),"Pending replacement destroyed the previous drawing");
        var expanded=new Envelope(new[]{new HullPoint(0,0),new HullPoint(20,0),new HullPoint(20,10),new HullPoint(0,10)},4);
        Publish(worker,entry,expanded);draw.ToggleSilhouetteDrawMode(true);
        Check(assignments==2&&!draw.silhouetteDrawPoints.SequenceEqual(drawn),"Published replacement was not adopted by the actual draw callback");
        ship.Destroy();Check(!worker.Read(entry,out _,out _,out _,out _),"Native destruction hook left a usable cache entry");
        HullDisplay.Reset();
        NativeRendererCases(ship);
        // Start a multi-frame capture, then close the native nav UI. Its arrays
        // and weak owner must be cleared without waiting for any worker.
        var pendingShip=Hull(Ship.Loaded.Shallow,Enumerable.Range(0,5000).Select(i=>new JsonItem{strName="floor",fX=i%100,fY=i/100}).ToArray());
        Field(pendingShip,"<objSS>k__BackingField",situ);
        var pendingDraw=new ShipDraw(pendingShip){lineBodySilhouette=NativeLine(5),silhouetteDrawPoints=new List<Vector2>(),SignalVisibility=Ostranauts.Ships.Sensors.SignalVisibility.Visible};
        pendingDraw.ToggleSilhouetteDrawMode(true);
        var pending=(HullSnapshot)AccessTools.Field(typeof(HullDisplay),"capture").GetValue(null);
        Check(pending!=null,"Large live snapshot did not yield across frames");
        AccessTools.Method(typeof(GUIOrbitDraw),"OnDestroy").Invoke(Empty<GUIOrbitDraw>(),null);
        Check(AccessTools.Field(typeof(HullDisplay),"capture").GetValue(null)==null&&!pending.Owner.TryGetTarget(out _)&&!worker.Valid(pending.Ticket),"Closing nav retained a partial snapshot/reservation");
        HullDisplay.Reset();
    }
    static Dictionary<string,T> Load<T>(string folder,Func<T,string> key) {
        var values=new Dictionary<string,T>();
        foreach(string file in Directory.GetFiles(Path.Combine(InstallationPaths.Data,folder),"*.json"))
            foreach(var item in JsonConvert.DeserializeObject<T[]>(File.ReadAllText(file)))values[key(item)]=item;
        return values;
    }
    static void AssetCases() {
        DataHandler.dictCOs=Load<JsonCondOwner>("condowners",x=>x.strName);
        DataHandler.dictCOOverlays=Load<JsonCOOverlay>("cooverlays",x=>x.strName);
        DataHandler.dictItemDefs=Load<JsonItemDef>("items",x=>x.strName);
        DataHandler.dictLoot=Load<JsonLoot>("loot",x=>x.strName).ToDictionary(p=>p.Key,p=>Loot.CloneFromJSON(p.Value));
        DataHandler.dictDataCOs=new Dictionary<string,DataCO>();
        var build=AccessTools.Method(typeof(DataHandler),"BuildDataCO");
        foreach(string name in new[]{"Light Tug","Heavy Tug 01","Argute Rapid Courier","Babak","ResAero01","ResAero02"}) {
            var asset=JArray.Parse(File.ReadAllText(Path.Combine(InstallationPaths.Data,"ships",name+".json")));
            var items=asset[0]["aItems"].ToObject<JsonItem[]>();
            foreach(string part in items.Select(i=>i.strName).Distinct()) {
                DataHandler.dictCOOverlays.TryGetValue(part,out var overlay);DataHandler.dictCOs.TryGetValue(part,out var basis);
                build.Invoke(null,new object[]{part,overlay,basis});
            }
            var timer=System.Diagnostics.Stopwatch.StartNew();var result=Take(Hull(Ship.Loaded.Shallow,items),out int steps);timer.Stop();
            Check(result.Count>=3,"Authored hull missing: "+name);
            Check(result.Collision!=null,"Shipped hull collision cover failed: "+name+" "+result.CollisionError);
            Console.WriteLine("Snapshot/worker asset "+name+": "+result.Count+" exterior vertices, "+result.Collision.Pieces.Length+" collision pieces, "+result.Loops+" components, "+steps+" capture steps, "+timer.Elapsed.TotalMilliseconds.ToString("F2")+" ms offline total.");
        }
    }
    static int Main(string[] args) {
        CoreChecks.Run();var harmony=new Harmony("offline.hull.integration");
        try {
            harmony.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Equality"),prefix:new HarmonyMethod(typeof(Program),nameof(Equal)));
            harmony.Patch(AccessTools.Method(typeof(UnityEngine.Object),"op_Inequality"),prefix:new HarmonyMethod(typeof(Program),nameof(Unequal)));
            harmony.Patch(AccessTools.Method(typeof(CondOwner),nameof(CondOwner.HasCond),new[]{typeof(string)}),prefix:new HarmonyMethod(typeof(Program),nameof(Has)));
            harmony.Patch(AccessTools.Method(typeof(CondOwner),nameof(CondOwner.GetCondAmount),new[]{typeof(string)}),prefix:new HarmonyMethod(typeof(Program),nameof(Amount)));
            harmony.Patch(AccessTools.Method(typeof(HullSnapshot),"BeginRecord"),transpiler:new HarmonyMethod(typeof(Program),nameof(EngineBoundary)));
            harmony.Patch(AccessTools.Method(typeof(HullDisplay),nameof(HullDisplay.Changed)),transpiler:new HarmonyMethod(typeof(Program),nameof(EngineBoundary)));
            harmony.Patch(AccessTools.Method(typeof(HullDisplay),nameof(HullDisplay.Assign)),prefix:new HarmonyMethod(typeof(Program),nameof(AssignBoundary)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(RectTransform),nameof(RectTransform.rect)),prefix:new HarmonyMethod(typeof(Program),nameof(PanelRect)));
            harmony.Patch(AccessTools.Method(typeof(ShipDraw),nameof(ShipDraw.ToggleSilhouetteDrawMode)),prefix:new HarmonyMethod(typeof(Program),nameof(ObserveToggle)){priority=Priority.Last+1});
            harmony.Patch(AccessTools.Method(typeof(GUIOrbitDraw),nameof(GUIOrbitDraw.DrawVectorLine)),prefix:new HarmonyMethod(typeof(Program),nameof(CaptureVectorLine)){priority=Priority.Last});
            foreach(var patch in new[]{typeof(HullIconPatch),typeof(HullDrawPatch),typeof(HullMutationPatch),typeof(HullRetirePatch),typeof(HullNavClosePatch),typeof(HullScalePatch)})harmony.CreateClassProcessor(patch).Patch();
            foreach(var method in new[]{AccessTools.Method(typeof(ShipDraw),nameof(ShipDraw.ToggleSilhouetteDrawMode)),AccessTools.Method(typeof(Ship),"UpdateTiles"),AccessTools.Method(typeof(Ship),nameof(Ship.Destroy)),AccessTools.Method(typeof(GUIOrbitDraw),"OnDestroy")})
                harmony.Patch(method,prefix:new HarmonyMethod(typeof(Program),nameof(Skip)){priority=Priority.Last});
            SnapshotCases();PresentationCases();AssetCases();RuntimeCollisionCases(harmony);DockingCases();ConsumerCases();
            if(args.Length==6&&args[0]=="docking-pair")SavedDockingPair(args[1],args[2],args[3],args[4],args[5]);
            else if(args.Length>2)SavedSizes(args[0],args[1],args[2]);
            else if(args.Length>0)SavedPreview(args[0],args.Length>1?args[1]:"O-GXD");
            Console.WriteLine(checks+" hull snapshot/asset/native-coordinate/render-copy checks passed. Unity mesh upload remains a live-scene check.");
            return 0;
        }finally{harmony.UnpatchSelf();HullDisplay.Reset(true);conditions.Clear();installed.Clear();positions.Clear();transforms.Clear();}
    }
}
namespace Ostrabus { static class OstrabusPlugin { internal static Log Logger=new Log(); } sealed class Log { internal void LogWarning(string message)=>throw new Exception(message); } }
