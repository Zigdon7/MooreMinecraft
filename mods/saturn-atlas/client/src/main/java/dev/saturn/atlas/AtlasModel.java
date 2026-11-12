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
    /** Tile coordinates are in units of {@code zoom} chunks; {@code maxZoom} is 1 for servers without zoom. */
    public record Snapshot(String world,double x,double z,int balance,List<Tile> tiles,List<Point> anchors,List<Point> markers,Quote quote,int zoom,int maxZoom) {
        public Snapshot(String world,double x,double z,int balance,List<Tile> tiles,List<Point> anchors,List<Point> markers,Quote quote){this(world,x,z,balance,tiles,anchors,markers,quote,1,1);}
        /** Blocks across the shorter side of the map. */
        public int span(){return 144*zoom;}
    }
    public Snapshot snapshot;
    public String selected="",notice="";
    public State state=State.IDLE;
    /** Zoom the open map asks for: 1 is closest, each step doubles the area. */
    public int zoom=1;
    public int maxZoom(){return snapshot==null?1:snapshot.maxZoom();}
    public boolean canZoomOut(){return zoom<maxZoom();}
    public boolean canZoomIn(){return zoom>1;}
    /** Steps the zoom (positive is out); false when already at the limit. */
    public boolean zoomBy(int direction){
        int next=direction>0?Math.min(maxZoom(),zoom*2):Math.max(1,zoom/2);
        if(next==zoom)return false;
        zoom=next;return true;
    }
    /** The open map follows the chosen zoom; the minimap always uses the closest view. */
    public String syncCommand(boolean mapOpen){return mapOpen&&zoom>1&&zoom<=maxZoom()?"atlas sync "+zoom:"atlas sync";}
    private long started;
    private long receivedAt;
    public void request(long now){started=now;state=State.WAITING;notice="Requesting map…";}
    public void tick(long now){if(state==State.WAITING&&now-started>=TIMEOUT_NANOS){state=State.TIMEOUT;notice="No server reply. Refresh to retry.";}}
    public void reset(){snapshot=null;selected="";notice="";state=State.IDLE;receivedAt=0;zoom=1;}
    public String freshness(long now){
        if(state==State.ERROR)return "Error: "+notice;
        if(state==State.TIMEOUT)return "Stale: no server reply";
        if(state==State.WAITING)return "Updating map";
        if(snapshot==null)return "No map data";
        return now-receivedAt>15_000_000_000L?"Stale: refresh map":"Map current";
    }
    public String describe(double x,double z,float yaw){
        if(snapshot==null)return "No map data. Refresh to retry.";
        String heading=switch(Math.floorMod(Math.round(yaw/90),4)){case 0->"south";case 1->"west";case 2->"north";default->"east";};
        return "Position "+(int)Math.floor(x)+", "+(int)Math.floor(z)+". Facing "+heading+". North is decreasing Z; east is increasing X. "+snapshot.anchors().size()+" lodestones and "+snapshot.markers().size()+" markers. ";
    }
    public static String pointPageLabel(int index,int count){
        if(index<0)return "Overview; "+count+" points available";
        if(count==0)return "No points available";
        return "Point "+Math.min(index+1,count)+" of "+count;
    }
    public static String describePoint(Point p,double x,double z){
        double dx=p.x()-x,dz=p.z()-z;String direction=Math.abs(dx)<1&&Math.abs(dz)<1?"here":(dz<=-1?"north":dz>=1?"south":"")+(dx<=-1?"west":dx>=1?"east":"");
        return p.name()+", "+Math.round(Math.hypot(dx,dz))+" blocks "+direction+", coordinates "+(int)Math.floor(p.x())+", "+(int)Math.floor(p.z());
    }
    public boolean canQuote(){return state==State.READY && snapshot!=null&&snapshot.anchors().stream().anyMatch(a->a.id().equals(selected));}
    /** Short, actionable explanation shown beside travel controls and in their tooltip. */
    public String travelHint(long now){
        if(!canQuote())return selected.isEmpty()?"Choose a destination":"Refresh map to quote";
        if(snapshot.quote()==null||!snapshot.quote().destination().equals(selected))return "Get a travel quote";
        if(now>=snapshot.quote().expiresNanos())return "Quote expired: get quote";
        if(snapshot.balance()<snapshot.quote().price())return "Need "+snapshot.quote().price()+" shards";
        return snapshot.quote().price()==0?"Travel: free":"Travel: "+snapshot.quote().price()+" shards";
    }
    public boolean canConfirm(long now){return canQuote()&&snapshot.quote()!=null&&snapshot.quote().destination().equals(selected)&&now<snapshot.quote().expiresNanos()&&snapshot.balance()>=snapshot.quote().price();}
    public void receive(String json,long now){
        try {
            if(json.length()>32000)throw new IllegalArgumentException("Reply too large");
            var root=JsonParser.parseString(json).getAsJsonObject();
            if(integer(root,"v",1,1)!=1)throw new IllegalArgumentException("Protocol version mismatch");
            if(root.has("error")){notice=bounded(root,"error",256);state=State.ERROR;return;}
            if(root.has("notice")){notice=bounded(root,"notice",256);return;}
            snapshot=parse(root,now);receivedAt=now;zoom=Math.min(zoom,snapshot.maxZoom());
            if(snapshot.anchors().stream().noneMatch(a->a.id().equals(selected)))selected="";
            state=State.READY;if(notice.isEmpty()||notice.startsWith("Requesting"))notice=snapshot.tiles().isEmpty()?"No terrain yet. Explore, then refresh.":"Shared map updated";
        }catch(RuntimeException e){state=State.ERROR;notice="Invalid map reply. Refresh to retry.";}
    }
    static Snapshot parse(JsonObject root,long now){
        String world=bounded(root,"world",128);double x=coordinate(root,"x"),z=coordinate(root,"z");
        int zoom=root.has("zoom")?integer(root,"zoom",1,16):1,maxZoom=root.has("max_zoom")?integer(root,"max_zoom",1,16):1;
        if(Integer.bitCount(zoom)!=1||Integer.bitCount(maxZoom)!=1||zoom>maxZoom)throw new IllegalArgumentException("Invalid zoom");
        int cx=Math.floorDiv((int)Math.floor(x),16*zoom),cz=Math.floorDiv((int)Math.floor(z),16*zoom);
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
            var q=root.getAsJsonObject("quote");String id=uuid(q,"destination");int price=integer(q,"price",0,64),remaining=integer(q,"remaining_ms",0,15000);
            if(anchors.stream().noneMatch(a->a.id().equals(id)))throw new IllegalArgumentException("Unknown quoted destination");
            quote=new Quote(id,price,now+remaining*1_000_000L);
        }
        return new Snapshot(world,x,z,integer(root,"balance",0,2304),List.copyOf(tiles),anchors,markers,quote,zoom,maxZoom);
    }
    static List<Point> points(JsonObject root,String field){List<Point> result=new ArrayList<>();Set<String> seen=new HashSet<>();for(var e:array(root,field,40)){var p=e.getAsJsonObject();String id=uuid(p,"id");if(!seen.add(id))throw new IllegalArgumentException("Duplicate marker");result.add(new Point(id,bounded(p,"name",40),coordinate(p,"x"),coordinate(p,"z")));}return List.copyOf(result);}
    static String uuid(JsonObject o,String key){String s=bounded(o,key,36);if(!s.matches("[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}"))throw new IllegalArgumentException("Invalid UUID");return s;}
    static JsonArray array(JsonObject o,String key,int max){var a=o.getAsJsonArray(key);if(a==null||a.size()>max)throw new IllegalArgumentException("Invalid array");return a;}
    static String bounded(JsonObject o,String key,int max){var value=o.get(key);if(value==null||!value.isJsonPrimitive()||!value.getAsJsonPrimitive().isString())throw new IllegalArgumentException("Invalid string");String s=value.getAsString();if(s.length()>max||s.chars().anyMatch(c->Character.isISOControl(c)||c=='§'))throw new IllegalArgumentException("Invalid string");return s;}
    static double coordinate(JsonObject o,String key){double n=o.get(key).getAsDouble();if(!Double.isFinite(n)||Math.abs(n)>30_000_000)throw new IllegalArgumentException("Invalid position");return n;}
    static int integer(JsonObject o,String key,int min,int max){double n=o.get(key).getAsDouble();if(!Double.isFinite(n)||n!=Math.rint(n)||n<min||n>max)throw new IllegalArgumentException("Invalid integer");return (int)n;}
}
