package dev.saturn.atlas;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;
import io.netty.buffer.Unpooled;
import net.minecraft.network.RegistryFriendlyByteBuf;
import net.minecraft.core.RegistryAccess;
import java.nio.file.*;
class AtlasWireTest {
 static Path fixtures(){return Path.of(System.getProperty("saturn.fixtures"));}
 @Test void actualClientCodecDecodesServerGeneratedUtf8Fixture() throws Exception {
  byte[] bytes=Files.readAllBytes(fixtures().resolve("atlas.bin"));
  var buf=new RegistryFriendlyByteBuf(Unpooled.wrappedBuffer(bytes),RegistryAccess.EMPTY);
  try{var payload=SaturnAtlasClient.AtlasPayload.CODEC.decode(buf);assertEquals(Files.readString(fixtures().resolve("atlas.json")),payload.json());assertEquals(0,buf.readableBytes());var model=new AtlasModel();model.receive(payload.json(),100);assertEquals(AtlasModel.State.READY,model.state);assertEquals("Café portal",model.snapshot.anchors().getFirst().name());model.selected=model.snapshot.anchors().getFirst().id();assertTrue(model.canConfirm(100));}finally{buf.release();}
 }
 @Test void actualCodecRejectsTruncatedPayload(){var buf=new RegistryFriendlyByteBuf(Unpooled.wrappedBuffer(new byte[]{10,123}),RegistryAccess.EMPTY);try{assertThrows(RuntimeException.class,()->SaturnAtlasClient.AtlasPayload.CODEC.decode(buf));}finally{buf.release();}}
 @Test void actualCodecRejectsOversizedDeclaredLength(){var buf=new RegistryFriendlyByteBuf(Unpooled.buffer(),RegistryAccess.EMPTY);try{buf.writeVarInt(100000);assertThrows(RuntimeException.class,()->SaturnAtlasClient.AtlasPayload.CODEC.decode(buf));}finally{buf.release();}}
 @Test void actualCodecRoundTripsIdenticalServerBytes() throws Exception {String json=Files.readString(fixtures().resolve("atlas.json"));var buf=new RegistryFriendlyByteBuf(Unpooled.buffer(),RegistryAccess.EMPTY);try{SaturnAtlasClient.AtlasPayload.CODEC.encode(buf,new SaturnAtlasClient.AtlasPayload(json));byte[] encoded=new byte[buf.readableBytes()];buf.readBytes(encoded);assertArrayEquals(Files.readAllBytes(fixtures().resolve("atlas.bin")),encoded);}finally{buf.release();}}
 @Test void actualRustCacheAndPresencePacketsDecodeWithoutNavigationRequests() throws Exception {
  var model=new AtlasModel();
  for(String name:new String[]{"atlas.bin","cache-begin.bin","cache-data.bin","players.bin"}){
   var buf=new RegistryFriendlyByteBuf(Unpooled.wrappedBuffer(Files.readAllBytes(fixtures().resolve(name))),RegistryAccess.EMPTY);
   try{model.receive(SaturnAtlasClient.AtlasPayload.CODEC.decode(buf).json(),100);assertEquals(0,buf.readableBytes());}finally{buf.release();}
  }
  assertEquals(AtlasModel.State.READY,model.state);assertFalse(model.cacheLoading);assertEquals(2,model.tilesAtZoom(1).size());assertEquals(2,model.tilesAtZoom(64).size());assertEquals("Café player",model.players(100).getFirst().name());assertTrue(model.players(5_000_000_101L).isEmpty());
 }
}
