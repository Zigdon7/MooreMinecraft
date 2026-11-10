package dev.saturn.atlas;

import com.google.gson.*;
import java.util.*;

/** Headless-testable protocol and control state. No Minecraft globals. */
public final class AtlasModel {
    public static final long TIMEOUT_NANOS=8_000_000_000L;
    public enum State { IDLE, WAITING, READY, ERROR, TIMEOUT }
    public record Tile(int x,int z,int[] pixels) {}
    public record Point(String id,String name,double x,double z) {}
    public record Quote(String destination,int price,long expiresNanos) {}
    public record Snapshot(String world,double x,double z,int balance,List<Tile> tiles,List<Point> anchors,List<Point> markers,Quote quote) {}
    public Snapshot snapshot;
    public String selected="",notice="";
    public State state=State.IDLE;
    private long started;
    public void request(long now){started=now;state=State.WAITING;notice="Requesting map…";}
    public void tick(long now){if(state==State.WAITING&&now-started>=TIMEOUT_NANOS){state=State.TIMEOUT;notice="No server reply. Refresh to retry.";}}
    public void reset(){snapshot=null;selected="";notice="";state=State.IDLE;}
    public boolean canQuote(){return state==State.READY && snapshot!=null&&snapshot.anchors().stream().anyMatch(a->a.id().equals(selected));}
    public boolean canConfirm(long now){return canQuote()&&snapshot.quote()!=null&&snapshot.quote().destination().equals(selected)&&now<snapshot.quote().expiresNanos()&&snapshot.balance()>=snapshot.quote().price();}
    public void receive(String json,long now){
        try {
            if(json.length()>32000)throw new IllegalArgumentException("Reply too large");
            var root=JsonParser.parseString(json).getAsJsonObject();
            if(integer(root,"v",1,1)!=1)throw new IllegalArgumentException("Protocol version mismatch");
            if(root.has("error")){notice=bounded(root,"error",256);state=State.ERROR;return;}
            if(root.has("notice")){notice=bounded(root,"notice",256);return;}
            snapshot=parse(root,now);
            if(snapshot.anchors().stream().noneMatch(a->a.id().equals(selected)))selected="";
            state=State.READY;if(notice.isEmpty()||notice.startsWith("Requesting"))notice=snapshot.tiles().isEmpty()?"No terrain yet. Explore, then refresh.":"Shared map updated";
        }catch(RuntimeException e){state=State.ERROR;notice="Invalid map reply. Refresh to retry.";}
    }
    static Snapshot parse(JsonObject root,long now){
        String world=bounded(root,"world",128);double x=coordinate(root,"x"),z=coordinate(root,"z");
        int cx=Math.floorDiv((int)Math.floor(x),16),cz=Math.floorDiv((int)Math.floor(z),16);
        List<Tile> tiles=new ArrayList<>();Set<String> seen=new HashSet<>();
        for(var entry:array(root,"tiles",81)){
            var t=entry.getAsJsonObject();int tx=integer(t,"x",-1875000,1875000),tz=integer(t,"z",-1875000,1875000);
            if(Math.abs((long)tx-cx)>4||Math.abs((long)tz-cz)>4||!seen.add(tx+":"+tz))throw new IllegalArgumentException("Tile out of viewport");
            var p=array(t,"pixels",16);if(p.size()!=16)throw new IllegalArgumentException("Invalid tile");int[] pixels=new int[16];
            for(int i=0;i<16;i++){double n=p.get(i).getAsDouble();if(!Double.isFinite(n)||n!=Math.rint(n)||n<0||n>255)throw new IllegalArgumentException("Invalid pixel");pixels[i]=(int)n;}
            tiles.add(new Tile(tx,tz,pixels));
        }
        var anchors=points(root,"anchors");var markers=points(root,"markers");Quote quote=null;
        if(root.has("quote")&&!root.get("quote").isJsonNull()){
            var q=root.getAsJsonObject("quote");String id=uuid(q,"destination");int price=integer(q,"price",1,64),remaining=integer(q,"remaining_ms",0,15000);
            if(anchors.stream().noneMatch(a->a.id().equals(id)))throw new IllegalArgumentException("Unknown quoted destination");
            quote=new Quote(id,price,now+remaining*1_000_000L);
        }
        return new Snapshot(world,x,z,integer(root,"balance",0,2304),List.copyOf(tiles),anchors,markers,quote);
    }
    static List<Point> points(JsonObject root,String field){List<Point> result=new ArrayList<>();Set<String> seen=new HashSet<>();for(var e:array(root,field,40)){var p=e.getAsJsonObject();String id=uuid(p,"id");if(!seen.add(id))throw new IllegalArgumentException("Duplicate marker");result.add(new Point(id,bounded(p,"name",40),coordinate(p,"x"),coordinate(p,"z")));}return List.copyOf(result);}
    static String uuid(JsonObject o,String key){String s=bounded(o,key,36);if(!s.matches("[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}"))throw new IllegalArgumentException("Invalid UUID");return s;}
    static JsonArray array(JsonObject o,String key,int max){var a=o.getAsJsonArray(key);if(a==null||a.size()>max)throw new IllegalArgumentException("Invalid array");return a;}
    static String bounded(JsonObject o,String key,int max){var value=o.get(key);if(value==null||!value.isJsonPrimitive()||!value.getAsJsonPrimitive().isString())throw new IllegalArgumentException("Invalid string");String s=value.getAsString();if(s.length()>max||s.chars().anyMatch(c->Character.isISOControl(c)||c=='§'))throw new IllegalArgumentException("Invalid string");return s;}
    static double coordinate(JsonObject o,String key){double n=o.get(key).getAsDouble();if(!Double.isFinite(n)||Math.abs(n)>30_000_000)throw new IllegalArgumentException("Invalid position");return n;}
    static int integer(JsonObject o,String key,int min,int max){double n=o.get(key).getAsDouble();if(!Double.isFinite(n)||n!=Math.rint(n)||n<min||n>max)throw new IllegalArgumentException("Invalid integer");return (int)n;}
}
