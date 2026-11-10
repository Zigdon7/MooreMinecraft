package dev.saturn.atlas;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
import net.minecraft.client.input.KeyEvent;
class AtlasInputViewportTest {
 @Test void defaultMappingIsMAndDoesNotMatchOtherKeys(){var key=SaturnAtlasClient.createOpenKey();assertEquals(com.mojang.blaze3d.platform.InputConstants.KEY_M,key.getDefaultKey().getValue());assertEquals("key.keyboard.m",key.getDefaultKey().getName());assertTrue(key.matches(new KeyEvent(com.mojang.blaze3d.platform.InputConstants.KEY_M,0,0)));assertFalse(key.matches(new KeyEvent(com.mojang.blaze3d.platform.InputConstants.KEY_N,0,0)));}
 @Test void mappingCanBeRebound(){var key=SaturnAtlasClient.createOpenKey();key.setKey(com.mojang.blaze3d.platform.InputConstants.Type.KEYBOARD.getOrCreate(com.mojang.blaze3d.platform.InputConstants.KEY_N));assertTrue(key.matches(new KeyEvent(com.mojang.blaze3d.platform.InputConstants.KEY_N,0,0)));assertFalse(key.matches(new KeyEvent(com.mojang.blaze3d.platform.InputConstants.KEY_M,0,0)));}
 @Test void largerScreenshotSizedMap(){var l=AtlasLayout.of(400,220);assertTrue(l.map().w()>200);assertTrue(l.map().h()>=150);assertTrue(l.map().w()*l.map().h()>108*108*2);}
 @Test void rectangularProjectionKeepsScaleAndCenter(){var v=AtlasViewport.of(0,0,220,156);assertEquals(v.scale()*16,v.x(24)-v.x(8),1);assertEquals(v.scale()*16,v.z(24)-v.z(8),1);assertEquals(110,v.x(8));assertEquals(78,v.z(8));}
 @Test void negativeCoordinateProjectionStaysCentered(){var v=AtlasViewport.of(-0.5,-16.5,220,156);assertEquals(110,v.x(-8));assertEquals(78,v.z(-24));assertTrue(v.contains(v.x(-0.5),v.z(-16.5),3));assertFalse(v.contains(10,156,3));}
}
