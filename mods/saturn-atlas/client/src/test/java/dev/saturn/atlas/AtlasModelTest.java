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
  for(String[] bad:new String[][]{{"3","16"},{"8","4"},{"32","32"},{"0","16"}}){var c=base();c.addProperty("zoom",Integer.parseInt(bad[0]));c.addProperty("max_zoom",Integer.parseInt(bad[1]));m=new AtlasModel();m.receive(c.toString(),0);assertEquals(AtlasModel.State.ERROR,m.state,bad[0]+"/"+bad[1]);}
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
}
