package dev.saturn.atlas;

import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.rendering.v1.hud.HudElementRegistry;
import net.fabricmc.fabric.api.client.keymapping.v1.KeyMappingHelper;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.minecraft.client.KeyMapping;
import net.minecraft.client.input.KeyEvent;
import com.mojang.blaze3d.platform.InputConstants;
import net.fabricmc.fabric.api.client.command.v2.ClientCommandRegistrationCallback;
import static net.fabricmc.fabric.api.client.command.v2.ClientCommands.literal;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayConnectionEvents;
import net.fabricmc.fabric.api.networking.v1.PayloadTypeRegistry;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.RegistryFriendlyByteBuf;
import net.minecraft.network.codec.StreamCodec;
import net.minecraft.network.protocol.common.custom.CustomPacketPayload;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;

/** Optional client; no authoritative pricing, permissions or teleport decisions. */
public final class SaturnAtlasClient implements ClientModInitializer {
    static final AtlasModel model=new AtlasModel();
    static KeyMapping openKey;
    static final AtlasHud hud=new AtlasHud();
    static KeyMapping createOpenKey(){return new KeyMapping("key.saturn_atlas.open",InputConstants.KEY_M,KeyMapping.Category.MISC);}
    static void openMap(){Minecraft.getInstance().gui.setScreen(new AtlasScreen());request("atlas sync");}
    static void request(String command){model.request(System.nanoTime());var client=Minecraft.getInstance();if(client.getConnection()!=null)client.getConnection().sendCommand(command);else model.receive("{\"v\":1,\"error\":\"Not connected to a server\"}",System.nanoTime());}
    public record AtlasPayload(String json) implements CustomPacketPayload {
        public static final Type<AtlasPayload> TYPE=new Type<>(Identifier.fromNamespaceAndPath("saturn","atlas_v1"));
        public static final StreamCodec<RegistryFriendlyByteBuf,AtlasPayload> CODEC=new StreamCodec<>() {
            public AtlasPayload decode(RegistryFriendlyByteBuf buf){return new AtlasPayload(buf.readUtf(32000));}
            public void encode(RegistryFriendlyByteBuf buf,AtlasPayload p){buf.writeUtf(p.json(),32000);}
        };
        @Override public Type<? extends CustomPacketPayload> type(){return TYPE;}
    }
    @Override public void onInitializeClient(){
        openKey=KeyMappingHelper.registerKeyMapping(createOpenKey());
        ClientTickEvents.END_CLIENT_TICK.register(client->{
            while(openKey.consumeClick())if(client.player!=null&&client.gui.screen()==null)openMap();
            long now=System.nanoTime();
            if(hud.due(now,client.player!=null&&client.getConnection()!=null,model.snapshot!=null,client.gui.screen()==null||client.gui.screen() instanceof AtlasScreen))client.getConnection().sendCommand("atlas sync");
        });
        HudElementRegistry.addLast(Identifier.fromNamespaceAndPath("saturn","minimap"),(graphics,delta)->drawMinimap(graphics));
        PayloadTypeRegistry.clientboundPlay().register(AtlasPayload.TYPE,AtlasPayload.CODEC);
        ClientPlayNetworking.registerGlobalReceiver(AtlasPayload.TYPE,(payload,context)->context.client().execute(()->{
            model.receive(payload.json(),System.nanoTime());hud.reply(System.nanoTime());
            if(Minecraft.getInstance().gui.screen() instanceof AtlasScreen screen)screen.rebuild();
        }));
        ClientPlayConnectionEvents.DISCONNECT.register((handler,client)->{model.reset();hud.reset();});
        ClientCommandRegistrationCallback.EVENT.register((dispatcher,access)->dispatcher.register(literal("saturnmap").executes(context->{Minecraft.getInstance().execute(()->{openMap();});return 1;})));
    }

    static void drawMinimap(GuiGraphicsExtractor g){
        var c=Minecraft.getInstance();var s=model.snapshot;
        if(c.player==null||c.gui.screen()!=null||c.gui.hud.isHidden()||s==null)return;
        var r=AtlasHud.bounds(g.guiWidth(),g.guiHeight());var v=AtlasViewport.of(c.player.getX(),c.player.getZ(),r.w(),r.h());
        g.fill(r.x()-2,r.y()-2,r.right()+2,r.bottom()+2,0xee4a6d7a);g.fill(r.x(),r.y(),r.right(),r.bottom(),0xee172735);
        g.enableScissor(r.x(),r.y(),r.right(),r.bottom());
        for(var t:s.tiles())for(int i=0;i<16;i++){int px=r.x()+v.x(t.x()*16+(i%4)*4),py=r.y()+v.z(t.z()*16+(i/4)*4);int cell=(int)Math.ceil(4*v.scale());g.fill(px,py,px+cell,py+cell,AtlasScreen.color(t.pixels()[i]));}
        for(var p:s.markers())AtlasScreen.point(g,p,r.x(),r.y(),v.startX(),v.startZ(),v.scale(),r.w(),r.h(),0xffffd56a);
        for(var p:s.anchors())AtlasScreen.point(g,p,r.x(),r.y(),v.startX(),v.startZ(),v.scale(),r.w(),r.h(),0xff8aeeff);
        int px=r.x()+v.x(c.player.getX()),py=r.y()+v.z(c.player.getZ());g.fill(px-1,py-1,px+2,py+2,0xffff6868);
        g.disableScissor();g.text(c.font,"N",r.x()+r.w()/2-3,r.y()+3,0xffffffff,true);g.text(c.font,"M: Atlas",r.x(),r.bottom()+4,0xffcde3ea,true);
    }
    static final class AtlasScreen extends Screen {
        AtlasLayout layout;Button quote,confirm;int page;
        AtlasScreen(){super(Component.literal("Saturn Atlas"));}
        @Override public boolean keyPressed(KeyEvent event){if(openKey!=null&&openKey.matches(event)){onClose();return true;}return super.keyPressed(event);}
        void rebuild(){clearWidgets();init();}
        Button add(String label,AtlasLayout.Rect r,Runnable action){return addRenderableWidget(Button.builder(Component.literal(label),b->action.run()).bounds(r.x(),r.y(),r.w(),r.h()).build());}
        @Override protected void init(){
            quote=null;confirm=null;layout=AtlasLayout.of(width,height);
            add("Refresh",layout.refresh(),()->request("atlas sync"));
            add("Close",layout.close(),this::onClose);
            if(layout.compact())return;
            add("Held Atlas",layout.held(),()->request("atlas view"));
            quote=add("Get quote",layout.quote(),()->request("lodestone quote "+model.selected));
            confirm=add("Travel",layout.confirm(),()->request("lodestone confirm"));
            if(model.snapshot!=null){
                int rows=Math.max(1,(layout.sidebar().h()-24)/23);var anchors=model.snapshot.anchors();
                int pages=Math.max(1,(anchors.size()+rows-1)/rows);page=Math.min(page,pages-1);
                for(int i=page*rows;i<Math.min(anchors.size(),(page+1)*rows);i++){
                    var a=anchors.get(i);String label=(a.id().equals(model.selected)?"> ":"")+a.name();int row=i-page*rows;
                    add(font.plainSubstrByWidth(label,layout.sidebar().w()-12),new AtlasLayout.Rect(layout.sidebar().x(),layout.sidebar().y()+row*23,layout.sidebar().w(),20),()->{model.selected=a.id();rebuild();});
                }
                if(pages>1){int y=layout.sidebar().bottom()-20;add("<",new AtlasLayout.Rect(layout.sidebar().x(),y,28,20),()->{page=Math.max(0,page-1);rebuild();});add(">",new AtlasLayout.Rect(layout.sidebar().right()-28,y,28,20),()->{page=Math.min(pages-1,page+1);rebuild();});}
            }
            controls();
        }
        void controls(){if(quote!=null)quote.active=model.canQuote();if(confirm!=null)confirm.active=model.canConfirm(System.nanoTime());}
        @Override public void tick(){super.tick();model.tick(System.nanoTime());controls();}
        void lines(GuiGraphicsExtractor g,String text,int x,int y,int maxWidth,int maxLines,int color){
            String remaining=text;
            for(int i=0;i<maxLines&&!remaining.isEmpty();i++){
                String line=font.plainSubstrByWidth(remaining,maxWidth);if(line.isEmpty())break;
                if(line.length()<remaining.length()){int space=line.lastIndexOf(' ');if(space>line.length()/2)line=line.substring(0,space);}
                g.text(font,line,x,y+i*11,color,false);remaining=remaining.substring(line.length()).stripLeading();
            }
        }
        @Override public void extractRenderState(GuiGraphicsExtractor g,int mouseX,int mouseY,float delta){
            g.fill(0,0,width,height,0xff111b28);super.extractRenderState(g,mouseX,mouseY,delta);
            g.fill(8,6,width-8,26,0xff1c3544);g.text(font,"SATURN ATLAS  ·  M / Esc to close",14,12,0xffa8edf0,true);
            if(layout.compact()){lines(g,"Window too small. Lower GUI Scale in Video Settings.",12,65,width-24,4,0xffffcf80);return;}
            var map=layout.map();int left=map.x(),top=map.y(),size=map.w();
            g.fill(left-1,top-1,map.right()+1,map.bottom()+1,0xff53737f);g.fill(left,top,map.right(),map.bottom(),0xff172735);
            g.text(font,"LODESTONES",layout.sidebar().x(),27,0xffa8edf0,false);
            if(model.snapshot!=null){
                var s=model.snapshot;var view=AtlasViewport.of(s.x(),s.z(),map.w(),map.h());
                double startX=view.startX(),startZ=view.startZ(),scale=view.scale();
                for(var t:s.tiles()){for(int i=0;i<16;i++){int px=(int)((t.x()*16+(i%4)*4-startX)*scale),py=(int)((t.z()*16+(i/4)*4-startZ)*scale);g.fill(left+px,top+py,Math.min(map.right(),left+px+(int)Math.ceil(4*scale)),Math.min(map.bottom(),top+py+(int)Math.ceil(4*scale)),color(t.pixels()[i]));}}
                for(var p:s.markers())point(g,p,left,top,startX,startZ,scale,map.w(),map.h(),0xffffd56a);
                for(var p:s.anchors())point(g,p,left,top,startX,startZ,scale,map.w(),map.h(),0xff8aeeff);
                int px=left+(int)((s.x()-startX)*scale),py=top+(int)((s.z()-startZ)*scale);g.fill(px-2,py-2,px+3,py+3,0xffff6868);
                if(s.anchors().isEmpty())lines(g,"No accessible lodestones. Place a lodestone, then use /lodestone create <name>.",layout.sidebar().x(),42,layout.sidebar().w(),4,0xffbac7d5);
                String cost=s.quote()!=null&&s.quote().destination().equals(model.selected)?"Quote: "+s.quote().price()+" shards":"Choose a destination";
                g.text(font,font.plainSubstrByWidth(cost,layout.sidebar().w()),layout.sidebar().x(),height-76,0xffffcf80,false);
                g.text(font,"Balance: "+s.balance()+" shards",layout.sidebar().x(),height-64,0xffbac7d5,false);
                if(s.tiles().isEmpty())lines(g,"No explored terrain yet. Explore nearby, then Refresh.",left+8,top+12,size-16,4,0xffbac7d5);
            }
            if(model.state!=AtlasModel.State.READY){
                g.fill(left+4,top+6,map.right()-4,top+63,0xf0111b28);
                String status=model.state==AtlasModel.State.WAITING?"Requesting shared map…":model.notice;
                lines(g,status,left+9,top+13,size-18,4,model.state==AtlasModel.State.ERROR||model.state==AtlasModel.State.TIMEOUT?0xffffad80:0xffd5e5ed);
            }
            // Error/waiting notices render inside the map; footer is reserved for controls.
        }
        static int color(int p){return switch(p){case 6->0xff77a84d;case 10->0xffd9ce92;case 34->0xffe5eff3;case 42->0xff8a6849;case 50->0xff466dae;case 18->0xffd16835;default->0xff8b8d91;};}
        static void point(GuiGraphicsExtractor g,AtlasModel.Point p,int left,int top,double sx,double sz,double scale,int mapWidth,int mapHeight,int color){
            int px=(int)((p.x()-sx)*scale),py=(int)((p.z()-sz)*scale);if(px<3||py<3||px>=mapWidth-3||py>=mapHeight-3)return;
            for(int d=-3;d<=3;d++){int e=3-Math.abs(d);g.fill(left+px+d,top+py-e,left+px+d+1,top+py-e+1,color);g.fill(left+px+d,top+py+e,left+px+d+1,top+py+e+1,color);}
            g.fill(left+px,top+py,left+px+1,top+py+1,0xffffffff);
        }
        @Override public boolean isPauseScreen(){return false;}
    }
}
