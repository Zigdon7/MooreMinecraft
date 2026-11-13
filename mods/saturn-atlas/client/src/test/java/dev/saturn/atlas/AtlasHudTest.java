package dev.saturn.atlas;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
class AtlasHudTest {
 @Test void minimapFitsTopRightAtSupportedSizes(){for(int[] s:new int[][]{{320,180},{400,220},{640,360},{1280,720}}){var r=AtlasHud.bounds(s[0],s[1]);assertEquals(8,s[0]-r.right());assertEquals(8,r.y());assertTrue(r.x()>s[0]/2);assertTrue(r.bottom()+14<s[1]);assertEquals(r.w(),r.h());}}
 @Test void unsupportedServersAreNotPolled(){var h=new AtlasHud();assertFalse(h.due(0,true,false,true));assertFalse(h.due(0,false,true,true));assertFalse(h.due(0,true,true,false));}
 @Test void neverPollsEvenAfterRepliesOrReconnect(){var h=new AtlasHud();for(long n:new long[]{0,5_000_000_000L,Long.MAX_VALUE})assertFalse(h.due(n,true,true,true));h.reply(0);h.reset();assertFalse(h.due(0,true,true,true));}
 @Test void configurableHudFitsAllCornersAndSizes(){
  for(int[] screen:new int[][]{{240,160},{320,180},{640,360}})for(int size=0;size<3;size++)for(int corner=0;corner<4;corner++){
   var r=AtlasHud.bounds(screen[0],screen[1],size,corner);assertTrue(r.x()>=8&&r.y()>=8);assertTrue(r.right()<=screen[0]-8);assertTrue(r.bottom()+22<=screen[1]);
   if(corner%2==1)assertEquals(8,r.x());else assertEquals(screen[0]-8,r.right());
  }
 }
 @Test void preferencesRoundTripAndMalformedValuesRecover(@org.junit.jupiter.api.io.TempDir java.nio.file.Path directory)throws Exception{
  var path=directory.resolve("atlas.properties");var p=AtlasPreferences.load(path);assertTrue(p.visible);assertFalse(p.text);
  p.visible=false;p.text=true;p.size=2;p.corner=3;p.save(path);var loaded=AtlasPreferences.load(path);
  assertFalse(loaded.visible);assertTrue(loaded.text);assertEquals(2,loaded.size);assertEquals(3,loaded.corner);
  java.nio.file.Files.writeString(path,"size=999\ncorner=-9\n");loaded=AtlasPreferences.load(path);assertEquals(2,loaded.size);assertEquals(0,loaded.corner);
  java.nio.file.Files.writeString(path,"size=not-a-number\n");assertEquals(1,AtlasPreferences.load(path).size);
  try(var files=java.nio.file.Files.list(directory)){assertEquals(1,files.count(),"Temporary files must not accumulate");}
 }

 @Test void unlimitedNavigationNeverRequestsNetworkRefresh(){var policy=new AtlasRefreshPolicy();assertTrue(policy.allow(AtlasRefreshPolicy.Event.OPEN,0));for(int i=0;i<10000;i++)for(var e:new AtlasRefreshPolicy.Event[]{AtlasRefreshPolicy.Event.ZOOM,AtlasRefreshPolicy.Event.PAN,AtlasRefreshPolicy.Event.CENTER,AtlasRefreshPolicy.Event.CLOSE,AtlasRefreshPolicy.Event.TICK})assertFalse(policy.allow(e,10_000_000_000L+i));assertTrue(policy.allow(AtlasRefreshPolicy.Event.MANUAL,20_000_000_000L));}
 @Test void rapidReopensAndRefreshClicksAreCoalesced(){var p=new AtlasRefreshPolicy();assertTrue(p.allow(AtlasRefreshPolicy.Event.OPEN,0));for(int i=1;i<2000;i++)assertFalse(p.allow(AtlasRefreshPolicy.Event.MANUAL,i*1_000_000L));assertTrue(p.allow(AtlasRefreshPolicy.Event.MANUAL,2_000_000_000L));p.reset();assertTrue(p.allow(AtlasRefreshPolicy.Event.OPEN,0));}
}
