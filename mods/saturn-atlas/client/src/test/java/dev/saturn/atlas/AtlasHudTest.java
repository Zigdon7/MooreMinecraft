package dev.saturn.atlas;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
class AtlasHudTest {
 @Test void minimapFitsTopRightAtSupportedSizes(){for(int[] s:new int[][]{{320,180},{400,220},{640,360},{1280,720}}){var r=AtlasHud.bounds(s[0],s[1]);assertEquals(8,s[0]-r.right());assertEquals(8,r.y());assertTrue(r.x()>s[0]/2);assertTrue(r.bottom()+14<s[1]);assertEquals(r.w(),r.h());}}
 @Test void unsupportedServersAreNotPolled(){var h=new AtlasHud();assertFalse(h.due(0,true,false,true));assertFalse(h.due(0,false,true,true));assertFalse(h.due(0,true,true,false));}
 @Test void updatesAreBoundedToOnePerFiveSeconds(){var h=new AtlasHud();assertTrue(h.due(0,true,true,true));assertFalse(h.due(4_999_999_999L,true,true,true));assertTrue(h.due(5_000_000_000L,true,true,true));h.reply(6_000_000_000L);assertFalse(h.due(10_000_000_000L,true,true,true));}
 @Test void disconnectResetsRefreshSchedule(){var h=new AtlasHud();h.reply(100);h.reset();assertTrue(h.due(0,true,true,true));}
}
