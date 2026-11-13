package dev.saturn.atlas;
import com.google.gson.*;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
class AtlasModelTest {
 static final String ID="11111111-1111-4111-8111-111111111111";
 static JsonObject base(){return JsonParser.parseString("{\"v\":1,\"world\":\"world\",\"x\":-0.5,\"z\":-16.5,\"balance\":3,\"tiles\":[],\"anchors\":[],\"markers\":[],\"quote\":null}").getAsJsonObject();}
 static JsonObject point(){return JsonParser.parseString("{\"id\":\""+ID+"\",\"name\":\"Home\",\"x\":0,\"z\":0}").getAsJsonObject();}
 @Test void blankMapIsAValidReplyNotInfiniteWaiting(){var m=new AtlasModel();m.request(10);m.receive(base().toString(),20);assertEquals(AtlasModel.State.READY,m.state);assertNotNull(m.snapshot);assertTrue(m.notice.contains("No terrain"));assertFalse(m.canQuote());assertFalse(m.canConfirm(20));}
 @Test void timeoutAndRetryRecover(){var m=new AtlasModel();m.request(0);m.tick(AtlasModel.TIMEOUT_NANOS-1);assertEquals(AtlasModel.State.WAITING,m.state);m.tick(AtlasModel.TIMEOUT_NANOS);assertEquals(AtlasModel.State.TIMEOUT,m.state);m.request(9_000_000_000L);m.receive(base().toString(),9_000_000_001L);assertEquals(AtlasModel.State.READY,m.state);}
 @Test void serverErrorIsVisibleAndDisablesTravel(){var m=new AtlasModel();m.receive("{\"v\":1,\"error\":\"Stand near an origin lodestone\"}",0);assertEquals(AtlasModel.State.ERROR,m.state);assertEquals("Stand near an origin lodestone",m.notice);assertFalse(m.canConfirm(0));}
 @Test void malformedRepliesDoNotDisappearSilently(){for(String json:new String[]{"{","{}","{\"v\":2}","{\"v\":1,\"world\":null}","[]"}){var m=new AtlasModel();m.receive(json,0);assertEquals(AtlasModel.State.ERROR,m.state,json);assertFalse(m.notice.isBlank());}}
 @Test void invalidCoordinatesAndPixelsRejected(){for(String replacement:new String[]{"NaN","1e100"}){var b=base();b.addProperty("x",replacement);var m=new AtlasModel();m.receive(b.toString(),0);assertEquals(AtlasModel.State.ERROR,m.state);}
 var b=base();var tile=JsonParser.parseString("{\"x\":-1,\"z\":-2,\"pixels\":[256,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]}");b.getAsJsonArray("tiles").add(tile);var m=new AtlasModel();m.receive(b.toString(),0);assertEquals(AtlasModel.State.ERROR,m.state);}
 @Test void quoteControlsRequireSelectionBalanceAndUnexpiredMatchingQuote(){var b=base();b.getAsJsonArray("anchors").add(point());b.add("quote",JsonParser.parseString("{\"destination\":\""+ID+"\",\"price\":2,\"remaining_ms\":10}"));var m=new AtlasModel();m.receive(b.toString(),100);assertFalse(m.canConfirm(100));m.selected=ID;assertTrue(m.canQuote());assertTrue(m.canConfirm(100));assertFalse(m.canConfirm(10_000_100));b.addProperty("balance",1);m.receive(b.toString(),100);assertFalse(m.canConfirm(100));}
 @Test void freeQuoteNeedsNoShards(){var b=base();b.addProperty("balance",0);b.getAsJsonArray("anchors").add(point());b.add("quote",JsonParser.parseString("{\"destination\":\""+ID+"\",\"price\":0,\"remaining_ms\":10}"));var m=new AtlasModel();m.receive(b.toString(),100);assertEquals(AtlasModel.State.READY,m.state);m.selected=ID;assertEquals("Travel: free",m.travelHint(100));assertTrue(m.canConfirm(100));assertFalse(m.canConfirm(10_000_100));b.getAsJsonObject("quote").addProperty("price",-1);m.receive(b.toString(),100);assertEquals(AtlasModel.State.ERROR,m.state);}
 @Test void widerZoomUsesTheZoomLimitFieldAndOlderRepliesStillWork(){
  var b=base();b.addProperty("zoom",1);b.addProperty("max_zoom",16);b.addProperty("zoom_limit",64);
  var m=new AtlasModel();m.receive(b.toString(),0);assertEquals(AtlasModel.State.READY,m.state);assertEquals(64,m.maxZoom());
  for(int i=0;i<6;i++)assertTrue(m.zoomBy(1));assertEquals(64,m.zoom);assertFalse(m.zoomBy(1));assertEquals("atlas sync 64",m.syncCommand(true));
  b.addProperty("zoom",64);b.addProperty("max_zoom",64);m.receive(b.toString(),0);assertEquals(AtlasModel.State.READY,m.state);assertEquals(9216,m.snapshot.span());
  var old=base();old.addProperty("zoom",1);old.addProperty("max_zoom",16);m.receive(old.toString(),0);assertEquals(16,m.maxZoom());assertEquals(16,m.zoom,"clamped to an older server's limit");
 }
 @Test void draggingIsOfferedOnlyWhenTheServerReportsTheViewCentre(){
  var m=new AtlasModel();m.receive(base().toString(),0);assertFalse(m.canPan());assertFalse(m.panBy(10,10));assertFalse(m.panned);assertEquals("atlas sync",m.syncCommand(true));
  var b=base();b.addProperty("x",100.5);b.addProperty("z",200.5);b.addProperty("zoom",1);b.addProperty("max_zoom",16);b.addProperty("cx",100);b.addProperty("cz",200);
  m.receive(b.toString(),0);assertEquals(AtlasModel.State.READY,m.state);assertTrue(m.canPan());assertFalse(m.viewOutdated());
  assertTrue(m.panBy(5,-5));assertTrue(m.panned);assertEquals(105,m.viewX);assertEquals(195,m.viewZ);assertFalse(m.viewOutdated(),"still inside the fetched tile ring");
  assertEquals("atlas sync 1 105 195",m.syncCommand(true));assertEquals("atlas sync",m.syncCommand(false),"minimap keeps following the player");
  assertTrue(m.panBy(-200.25,0));assertTrue(m.viewOutdated());assertEquals("atlas sync 1 -96 195",m.syncCommand(true));
  assertTrue(m.zoomBy(1));assertEquals("atlas sync 2 -96 195",m.syncCommand(true));
  m.recenter();assertFalse(m.panned);assertFalse(m.viewOutdated());assertEquals("atlas sync 2",m.syncCommand(true));
  m.panBy(1,1);m.receive(base().toString(),0);assertFalse(m.panned,"a server without dragging drops the pan");m.reset();assertFalse(m.panned);
 }
 @Test void pannedTilesAreValidatedAroundTheReportedCentreNotThePlayer(){
  var b=base();b.addProperty("x",0.5);b.addProperty("z",0.5);b.addProperty("zoom",1);b.addProperty("max_zoom",16);b.addProperty("cx",1600);b.addProperty("cz",-1600);
  var t=JsonParser.parseString("{\"x\":104,\"z\":-96,\"pixels\":[6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6]}").getAsJsonObject();b.getAsJsonArray("tiles").add(t);
  var m=new AtlasModel();m.receive(b.toString(),0);assertEquals(AtlasModel.State.READY,m.state);assertEquals(1600,m.snapshot.centerX());assertEquals(0.5,m.snapshot.x());
  t.addProperty("x",0);m.receive(b.toString(),0);assertEquals(AtlasModel.State.ERROR,m.state,"tiles around the player are outside a panned view");
  var c=base();c.addProperty("cx",1e100);c.addProperty("cz",0);m=new AtlasModel();m.receive(c.toString(),0);assertEquals(AtlasModel.State.ERROR,m.state);
 }
 @Test void zoomIsOfferedOnlyWhenTheServerSupportsItAndIsBounded(){
  var m=new AtlasModel();m.receive(base().toString(),0);assertEquals(1,m.maxZoom());assertFalse(m.canZoomOut());assertFalse(m.zoomBy(1));assertEquals("atlas sync",m.syncCommand(true));
  var b=base();b.addProperty("zoom",1);b.addProperty("max_zoom",16);m.receive(b.toString(),0);assertEquals(144,m.snapshot.span());
  assertTrue(m.zoomBy(1));assertEquals("atlas sync 2",m.syncCommand(true));assertEquals("atlas sync",m.syncCommand(false),"minimap keeps the closest view");
  assertTrue(m.zoomBy(1));assertTrue(m.zoomBy(1));assertTrue(m.zoomBy(1));assertEquals(16,m.zoom);assertFalse(m.zoomBy(1));assertFalse(m.canZoomOut());
  assertTrue(m.zoomBy(-1));assertEquals(8,m.zoom);m.zoom=1;assertFalse(m.zoomBy(-1));assertFalse(m.canZoomIn());
  m.zoom=8;m.receive(base().toString(),0);assertEquals(1,m.zoom,"a server without zoom resets the request");m.zoom=4;m.reset();assertEquals(1,m.zoom);
 }
 @Test void zoomedTilesAreValidatedInZoomedTileUnits(){
  var b=base();b.addProperty("x",1000.5);b.addProperty("z",-1000.5);b.addProperty("zoom",4);b.addProperty("max_zoom",16);
  var t=JsonParser.parseString("{\"x\":15,\"z\":-16,\"pixels\":[6,0,6,6,6,6,6,6,6,6,6,6,6,6,6,0]}").getAsJsonObject();b.getAsJsonArray("tiles").add(t);
  var m=new AtlasModel();m.receive(b.toString(),0);assertEquals(AtlasModel.State.READY,m.state);assertEquals(4,m.snapshot.zoom());assertEquals(576,m.snapshot.span());assertEquals(0,m.snapshot.tiles().getFirst().pixels()[1]);
  t.addProperty("x",62);m.receive(b.toString(),0);assertEquals(AtlasModel.State.ERROR,m.state,"chunk coordinates are out of range at zoom 4");
  for(String[] bad:new String[][]{{"3","16"},{"8","4"},{"128","128"},{"0","16"}}){var c=base();c.addProperty("zoom",Integer.parseInt(bad[0]));c.addProperty("max_zoom",Integer.parseInt(bad[1]));m=new AtlasModel();m.receive(c.toString(),0);assertEquals(AtlasModel.State.ERROR,m.state,bad[0]+"/"+bad[1]);}
 }
 @Test void removedSelectionIsCleared(){var b=base();b.getAsJsonArray("anchors").add(point());var m=new AtlasModel();m.receive(b.toString(),0);m.selected=ID;assertTrue(m.canQuote());m.receive(base().toString(),0);assertEquals("",m.selected);assertFalse(m.canQuote());}
 @Test void duplicateOrInjectionDestinationsRejected(){var b=base();var a=point();a.addProperty("id","foo;op player");b.getAsJsonArray("anchors").add(a);var m=new AtlasModel();m.receive(b.toString(),0);assertEquals(AtlasModel.State.ERROR,m.state);b=base();b.getAsJsonArray("anchors").add(point());b.getAsJsonArray("anchors").add(point());m.receive(b.toString(),0);assertEquals(AtlasModel.State.ERROR,m.state);}
 @Test void outOfViewportAndDuplicateTilesRejected(){var b=base();var t=JsonParser.parseString("{\"x\":-1,\"z\":-2,\"pixels\":[6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6]}").getAsJsonObject();b.getAsJsonArray("tiles").add(t);var m=new AtlasModel();m.receive(b.toString(),0);assertEquals(AtlasModel.State.READY,m.state);b.getAsJsonArray("tiles").add(t);m.receive(b.toString(),0);assertEquals(AtlasModel.State.ERROR,m.state);t.addProperty("x",10000);b.getAsJsonArray("tiles").remove(1);m.receive(b.toString(),0);assertEquals(AtlasModel.State.ERROR,m.state);}
 @Test void disconnectedStateCannotLeakAnotherServersMap(){var m=new AtlasModel();m.receive(base().toString(),0);m.reset();assertNull(m.snapshot);assertEquals(AtlasModel.State.IDLE,m.state);assertFalse(m.canConfirm(0));}
 @Test void replySizeIsBounded(){var m=new AtlasModel();m.receive("x".repeat(32001),0);assertEquals(AtlasModel.State.ERROR,m.state);}
 @org.junit.jupiter.api.Test void travelHintExplainsEveryBlockedAction(){
  var m=new AtlasModel();assertEquals("Choose a destination",m.travelHint(0));
  var p=new AtlasModel.Point("a","Home",0,0);m.selected="a";
  m.snapshot=new AtlasModel.Snapshot("world",0,0,0,java.util.List.of(),java.util.List.of(p),java.util.List.of(),null);
  assertEquals("Refresh map to quote",m.travelHint(0));m.state=AtlasModel.State.READY;
  assertEquals("Get a travel quote",m.travelHint(0));
  m.snapshot=new AtlasModel.Snapshot("world",0,0,0,java.util.List.of(),java.util.List.of(p),java.util.List.of(),new AtlasModel.Quote("a",3,100));
  assertEquals("Need 3 shards",m.travelHint(0));assertFalse(m.canConfirm(0));
  assertEquals("Quote expired: get quote",m.travelHint(100));
  m.snapshot=new AtlasModel.Snapshot("world",0,0,4,java.util.List.of(),java.util.List.of(p),java.util.List.of(),new AtlasModel.Quote("a",3,100));
  assertEquals("Travel: 3 shards",m.travelHint(0));assertTrue(m.canConfirm(0));
 }
 @Test void staleAndFailedSnapshotsRemainExplicitAndRecover(){
  var m=new AtlasModel();assertEquals("No map data",m.freshness(0));m.receive(base().toString(),100);
  assertEquals("Map current",m.freshness(100));assertEquals("Stale: refresh map",m.freshness(15_000_000_101L));
  m.request(20_000_000_000L);assertEquals("Updating map",m.freshness(20_000_000_001L));m.tick(28_000_000_000L);
  assertEquals("Stale: no server reply",m.freshness(28_000_000_000L));assertNotNull(m.snapshot);
  m.receive("{",30_000_000_000L);assertTrue(m.freshness(30_000_000_000L).startsWith("Error:"));assertNotNull(m.snapshot);
  m.receive(base().toString(),31_000_000_000L);assertEquals("Map current",m.freshness(31_000_000_000L));
 }
 @Test void textualMapDescribesHeadingDirectionDistanceAndNegativeCoordinates(){
  var m=new AtlasModel();m.receive(base().toString(),0);
  assertTrue(m.describe(-0.5,-16.5,0).contains("Position -1, -17. Facing south"));
  assertTrue(m.describe(0,0,-90).contains("Facing east"));assertTrue(m.describe(0,0,180).contains("Facing north"));
  var p=new AtlasModel.Point("id","Long marker name",-3,-4);
  assertEquals("Long marker name, 5 blocks northwest, coordinates -3, -4",AtlasModel.describePoint(p,0,0));
  assertTrue(AtlasModel.describePoint(new AtlasModel.Point("id","Here",0,0),0,0).contains("blocks here"));
 }
 @Test void overviewNarrationNeverClaimsPointZero(){
  assertEquals("Overview; 12 points available",AtlasModel.pointPageLabel(-1,12));
  assertEquals("Overview; 0 points available",AtlasModel.pointPageLabel(-1,0));
  assertEquals("Point 1 of 12",AtlasModel.pointPageLabel(0,12));
  assertEquals("Point 12 of 12",AtlasModel.pointPageLabel(99,12));
  assertEquals("No points available",AtlasModel.pointPageLabel(0,0));
 }

 static JsonObject beginCache(String token,int count){var b=new JsonObject();b.addProperty("v",1);b.addProperty("world","world");b.addProperty("cache","begin");b.addProperty("token",token);b.addProperty("count",count);b.addProperty("batches",(count+95)/96);return b;}
 static JsonObject cacheTile(int x,int z){var t=new JsonObject();t.addProperty("x",x);t.addProperty("z",z);var p=new JsonArray();for(int i=0;i<16;i++)p.add(6);t.add("pixels",p);return t;}
 static JsonObject cacheData(String token,int batch,JsonObject... tiles){var d=new JsonObject();d.addProperty("v",1);d.addProperty("world","world");d.addProperty("cache","data");d.addProperty("token",token);d.addProperty("batch",batch);var a=new JsonArray();for(var t:tiles)a.add(t);d.add("tiles",a);return d;}
 static AtlasModel cached(){var m=new AtlasModel();m.receive(base().toString(),0);m.receive(beginCache("a",2).toString(),1);m.receive(cacheData("a",0,cacheTile(-81,67),cacheTile(-52,-42)).toString(),2);return m;}
 @Test void fullCacheKeepsRemoteSharedTerrainAtEveryLocalZoom(){var m=cached();assertFalse(m.cacheLoading);assertEquals(2,m.tilesAtZoom(1).size());for(int zoom:new int[]{1,2,4,8,16,32,64}){var tiles=m.tilesAtZoom(zoom);assertFalse(tiles.isEmpty());for(int[] point:new int[][]{{-81,67},{-52,-42}}){int tx=Math.floorDiv(point[0],zoom),tz=Math.floorDiv(point[1],zoom);assertTrue(tiles.stream().anyMatch(t->t.x()==tx&&t.z()==tz&&java.util.Arrays.stream(t.pixels()).anyMatch(c->c!=0)));}assertEquals(zoom,m.dataZoom(zoom));}}
 @Test void cacheReplacementIsAtomicAcrossBatches(){var m=cached();m.receive(beginCache("b",97).toString(),3);var first=new JsonObject[96];for(int i=0;i<96;i++)first[i]=cacheTile(i,0);m.receive(cacheData("b",0,first).toString(),4);assertTrue(m.cacheLoading);assertEquals(2,m.tilesAtZoom(1).size());m.receive(cacheData("b",1,cacheTile(96,0)).toString(),5);assertEquals(97,m.tilesAtZoom(1).size());assertFalse(m.cacheLoading);assertEquals(AtlasModel.State.READY,m.state);}
 @Test void invalidCacheDoesNotReplaceGoodTerrain(){var m=cached();m.receive(beginCache("bad",2).toString(),3);m.receive(cacheData("bad",0,cacheTile(1,1),cacheTile(1,1)).toString(),4);assertEquals(AtlasModel.State.ERROR,m.state);assertEquals(2,m.tilesAtZoom(1).size());assertFalse(m.cacheLoading);}
 @Test void wrongWorldAndOversizedCachesAreRejected(){var m=cached();var b=beginCache("bad",65537);m.receive(b.toString(),3);assertEquals(AtlasModel.State.ERROR,m.state);assertEquals(2,m.tilesAtZoom(1).size());b=beginCache("bad",1);b.addProperty("world","nether");m.receive(b.toString(),4);assertEquals(AtlasModel.State.ERROR,m.state);}
 @Test void staleTokenIsIgnoredAndCacheTimeoutKeepsOldTerrain(){var m=cached();m.receive(beginCache("b",1).toString(),3);m.receive(cacheData("a",0,cacheTile(1,1)).toString(),4);assertTrue(m.cacheLoading);m.tick(AtlasModel.TIMEOUT_NANOS+4);assertFalse(m.cacheLoading);assertEquals(AtlasModel.State.TIMEOUT,m.state);assertEquals(2,m.tilesAtZoom(1).size());}
 @Test void metadataRepliesDontRefreshTerrainAndDisconnectClearsIt(){var m=cached();m.receive(base().toString(),3);assertEquals(2,m.tilesAtZoom(1).size());m.reset();assertTrue(m.tilesAtZoom(1).isEmpty());assertNull(m.snapshot);assertEquals(AtlasModel.State.IDLE,m.state);}
 @Test void dimensionChangeAndEmptyCacheClearPreviousTerrain(){var m=cached();m.receive(beginCache("empty",0).toString(),3);assertTrue(m.tilesAtZoom(1).isEmpty());m=cached();var b=base();b.addProperty("world","other");m.receive(b.toString(),4);assertTrue(m.tilesAtZoom(1).isEmpty());}
 @Test void playerPresenceDoesNotRefreshTerrainAndClearsOnDimensionAndDisconnect(){
  var m=new AtlasModel();String snap="{\"v\":1,\"world\":\"world\",\"x\":0,\"z\":0,\"balance\":0,\"tiles\":[],\"anchors\":[],\"markers\":[]}";
  m.receive(snap,100);var before=m.snapshot;String roster="{\"v\":1,\"roster\":true,\"world\":\"world\",\"players\":[{\"id\":\"11111111-1111-4111-8111-111111111111\",\"name\":\"Other\",\"x\":-16,\"z\":16}]}";
  m.receive(roster,200);assertSame(before,m.snapshot);assertEquals(1,m.players(200).size());m.receive(roster.replace("\"world\":\"world\"","\"world\":\"nether\""),300);assertTrue(m.players(300).isEmpty());m.receive(roster,400);m.reset();assertTrue(m.players(400).isEmpty());
 }
}
