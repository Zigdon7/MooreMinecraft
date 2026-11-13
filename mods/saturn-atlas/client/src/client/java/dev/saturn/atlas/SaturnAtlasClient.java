package dev.saturn.atlas;

import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.rendering.v1.hud.HudElementRegistry;
import net.fabricmc.fabric.api.client.keymapping.v1.KeyMappingHelper;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.minecraft.client.KeyMapping;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import com.mojang.blaze3d.platform.InputConstants;
import net.fabricmc.fabric.api.client.command.v2.ClientCommandRegistrationCallback;
import static net.fabricmc.fabric.api.client.command.v2.ClientCommands.literal;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayConnectionEvents;
import net.fabricmc.fabric.api.networking.v1.PayloadTypeRegistry;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.RegistryFriendlyByteBuf;
import net.minecraft.network.codec.StreamCodec;
import net.minecraft.network.protocol.common.custom.CustomPacketPayload;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;

/** Optional client; no authoritative pricing, permissions or teleport decisions. */
public final class SaturnAtlasClient implements ClientModInitializer {
    static final AtlasModel model=new AtlasModel();
    static KeyMapping openKey,describeKey,toggleHudKey;
    static AtlasPreferences preferences=new AtlasPreferences();
    static java.nio.file.Path preferencesFile;
    static String preferenceNotice="";
    static void savePreferences(){try{preferences.save(preferencesFile);preferenceNotice="Saved locally";}catch(java.io.IOException e){preferenceNotice="Could not save HUD settings; changes apply this session only";}}
    static String describePlayer(){var c=Minecraft.getInstance();return c.player==null?"Not connected":model.describe(c.player.getX(),c.player.getZ(),c.player.getYRot());}
    static final AtlasHud hud=new AtlasHud();
    static final AtlasRefreshPolicy refreshPolicy=new AtlasRefreshPolicy();
    static void refreshMap(AtlasRefreshPolicy.Event reason){if(refreshPolicy.allow(reason,System.nanoTime()))request("atlas sync cache");}
    static KeyMapping createOpenKey(){return new KeyMapping("key.saturn_atlas.open",InputConstants.KEY_M,KeyMapping.Category.MISC);}
    /** Unbound by default. Minecraft 26.3 uses 0 for "no key"; -1 is read as a real key and crashes on mouse grab. */
    static KeyMapping createUnboundKey(String name){return new KeyMapping(name,InputConstants.UNKNOWN.getValue(),KeyMapping.Category.MISC);}
    /** Unbinds a mapping whose saved key is not a valid keyboard key (written by 0.1.3-dev builds). */
    static boolean repairKey(KeyMapping mapping){
        var bound=InputConstants.getKey(mapping.saveString());
        if(bound.getType()!=InputConstants.Type.KEYBOARD||bound.getValue()>=0)return false;
        mapping.setKey(InputConstants.UNKNOWN);return true;
    }
    static void openMap(){Minecraft.getInstance().gui.setScreen(new AtlasScreen());refreshMap(AtlasRefreshPolicy.Event.OPEN);}
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
        preferencesFile=net.fabricmc.loader.api.FabricLoader.getInstance().getConfigDir().resolve("saturn-atlas.properties");
        preferences=AtlasPreferences.load(preferencesFile);
        openKey=KeyMappingHelper.registerKeyMapping(createOpenKey());
        describeKey=KeyMappingHelper.registerKeyMapping(createUnboundKey("key.saturn_atlas.describe"));
        toggleHudKey=KeyMappingHelper.registerKeyMapping(createUnboundKey("key.saturn_atlas.toggle_hud"));
        net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientLifecycleEvents.CLIENT_STARTED.register(client->{
            boolean repaired=false;for(var key:new KeyMapping[]{openKey,describeKey,toggleHudKey})repaired|=repairKey(key);
            if(repaired){KeyMapping.resetMapping();client.options.save();}
        });
        ClientTickEvents.END_CLIENT_TICK.register(client->{
            while(openKey.consumeClick())if(client.player!=null&&client.gui.screen()==null)openMap();
            while(toggleHudKey.consumeClick()){preferences.visible=!preferences.visible;savePreferences();}
            while(describeKey.consumeClick())if(client.player!=null&&client.gui.screen()==null)client.gui.setScreen(new AtlasDetailsScreen(null,-1));
            long now=System.nanoTime();
            model.tick(now);
            // No background polling: terrain changes only on map open or explicit Refresh.
        });
        HudElementRegistry.addLast(Identifier.fromNamespaceAndPath("saturn","minimap"),(graphics,delta)->drawMinimap(graphics));
        PayloadTypeRegistry.clientboundPlay().register(AtlasPayload.TYPE,AtlasPayload.CODEC);
        ClientPlayNetworking.registerGlobalReceiver(AtlasPayload.TYPE,(payload,context)->context.client().execute(()->{
            model.receive(payload.json(),System.nanoTime());if(model.cacheLoading||payload.json().contains("\"roster\""))return;
            if(Minecraft.getInstance().gui.screen() instanceof AtlasScreen screen)screen.rebuild();
            else if(Minecraft.getInstance().gui.screen() instanceof AtlasDetailsScreen screen)screen.rebuild();
        }));
        ClientPlayConnectionEvents.DISCONNECT.register((handler,client)->{model.reset();hud.reset();refreshPolicy.reset();});
        ClientCommandRegistrationCallback.EVENT.register((dispatcher,access)->dispatcher.register(literal("saturnmap").executes(context->{Minecraft.getInstance().execute(()->{openMap();});return 1;})));
    }

    static void drawMinimap(GuiGraphicsExtractor g){
        var c=Minecraft.getInstance();var s=model.snapshot;
        if(c.player==null||c.gui.screen()!=null||c.gui.hud.isHidden()||s==null||!preferences.visible)return;
        var r=AtlasHud.bounds(g.guiWidth(),g.guiHeight(),preferences.size,preferences.corner);
        if(preferences.text){
            int textWidth=Math.min(200,g.guiWidth()-16),tx=preferences.corner%2==1?8:g.guiWidth()-textWidth-8,ty=preferences.corner>=2?g.guiHeight()-54:8;
            g.fill(tx-2,ty-2,tx+textWidth+2,ty+45,0xee111b28);
            g.text(c.font,c.font.plainSubstrByWidth(model.freshness(System.nanoTime()),textWidth),tx,ty,0xffffffff,true);
            g.text(c.font,c.font.plainSubstrByWidth("X "+(int)c.player.getX()+"  Z "+(int)c.player.getZ(),textWidth),tx,ty+12,0xffedf5fa,true);
            var points=new java.util.ArrayList<AtlasModel.Point>(s.anchors());points.addAll(s.markers());
            points.stream().min(java.util.Comparator.comparingDouble(p->Math.hypot(p.x()-c.player.getX(),p.z()-c.player.getZ()))).ifPresent(p->g.text(c.font,c.font.plainSubstrByWidth(AtlasModel.describePoint(p,c.player.getX(),c.player.getZ()),textWidth),tx,ty+24,0xffedf5fa,true));
            g.text(c.font,c.font.plainSubstrByWidth(openHint()+": Atlas > Text for all points",textWidth),tx,ty+36,0xffedf5fa,true);return;
        }
        var v=AtlasViewport.of(c.player.getX(),c.player.getZ(),r.w(),r.h(),1);int step=4*model.dataZoom(1);
        g.fill(r.x()-2,r.y()-2,r.right()+2,r.bottom()+2,0xee4a6d7a);g.fill(r.x(),r.y(),r.right(),r.bottom(),0xee172735);
        g.enableScissor(r.x(),r.y(),r.right(),r.bottom());
        for(var t:model.tilesAtZoom(1)){if((t.x()+1)*4.0*step<v.startX()||t.x()*4.0*step>v.startX()+r.w()/v.scale()||(t.z()+1)*4.0*step<v.startZ()||t.z()*4.0*step>v.startZ()+r.h()/v.scale())continue;for(int i=0;i<16;i++){if(t.pixels()[i]==0)continue;int px=r.x()+v.x((t.x()*4+i%4)*step),py=r.y()+v.z((t.z()*4+i/4)*step);int cell=(int)Math.ceil(step*v.scale());g.fill(px,py,px+cell,py+cell,AtlasScreen.color(t.pixels()[i]));}}
        for(var p:s.markers())AtlasScreen.point(g,p,r.x(),r.y(),v.startX(),v.startZ(),v.scale(),r.w(),r.h(),0xffffd56a);
        for(var p:model.players(System.nanoTime()))AtlasScreen.otherPlayer(g,p,r.x(),r.y(),v.startX(),v.startZ(),v.scale(),r.w(),r.h());
        for(var p:s.anchors())AtlasScreen.point(g,p,r.x(),r.y(),v.startX(),v.startZ(),v.scale(),r.w(),r.h(),0xff8aeeff);
        int px=r.x()+v.x(c.player.getX()),py=r.y()+v.z(c.player.getZ());AtlasScreen.player(g,px,py);
        g.disableScissor();
        g.fill(r.x(),r.y(),r.right(),r.y()+11,0xdd111b28);
        g.text(c.font,c.font.plainSubstrByWidth(model.freshness(System.nanoTime()),r.w()-4),r.x()+2,r.y()+2,0xffffffff,true);
        g.text(c.font,"N",r.x()+r.w()/2-3,r.y()+14,0xffffffff,true);g.text(c.font,c.font.plainSubstrByWidth(openHint()+": Atlas",r.w()),r.x(),r.bottom()+4,0xffe1edf2,true);
    }
    static String openHint(){return openKey==null?"M":openKey.getTranslatedKeyMessage().getString();}
    static abstract class AtlasUtilityScreen extends Screen {
        final Screen parent;
        AtlasUtilityScreen(String title,Screen parent){super(Component.literal(title));this.parent=parent;}
        Button button(String label,int x,int y,int w,Runnable action){return addRenderableWidget(Button.builder(Component.literal(label),b->action.run()).bounds(x,y,w,20).build());}
        Button button(String label,AtlasLayout.Rect r,Runnable action){return button(label,r.x(),r.y(),r.w(),action);}
        @Override public void onClose(){Minecraft.getInstance().gui.setScreen(parent);}
        void wrapped(GuiGraphicsExtractor g,String text,int x,int y,int w,int maxLines){
            for(var line:font.split(Component.literal(text),Math.max(1,w))){if(maxLines--<=0)break;g.text(font,line,x,y,0xffedf5fa,false);y+=11;}
        }
        boolean tinyFallback(){
            if(AtlasLayout.utilitySupported(width,height))return false;
            button("Back",AtlasLayout.utilityControls(width,height,false)[0],this::onClose);return true;
        }
        boolean drawTinyFallback(GuiGraphicsExtractor g){
            if(AtlasLayout.utilitySupported(width,height))return false;
            if(height>=90)wrapped(g,"This view requires 240 x 150 GUI units. Lower GUI Scale or enlarge the window.",12,34,width-24,Math.max(0,(height-64)/11));
            return true;
        }
        void background(GuiGraphicsExtractor g){g.fill(0,0,width,height,0xff111b28);if(height>=70)g.text(font,title,12,12,0xffd8f9ff,true);}
        @Override public boolean isPauseScreen(){return false;}
    }
    static final class AtlasOptionsScreen extends AtlasUtilityScreen {
        AtlasOptionsScreen(Screen parent){super("Atlas HUD settings",parent);}
        void changed(){int focus=children().indexOf(getFocused());savePreferences();clearWidgets();init();if(focus>=0&&focus<children().size())setFocused(children().get(focus));scheduleNarration();}
        @Override protected void init(){
            if(tinyFallback())return;
            var r=AtlasLayout.utilityControls(width,height,true);
            button("HUD: "+(preferences.visible?"Shown":"Hidden"),r[0],()->{preferences.visible=!preferences.visible;changed();});
            button("Mode: "+(preferences.text?"Text":"Map"),r[1],()->{preferences.text=!preferences.text;changed();});
            button("Size: "+new String[]{"Small","Medium","Large"}[preferences.size],r[2],()->{preferences.size=(preferences.size+1)%3;changed();});
            button(preferences.cornerName(),r[3],()->{preferences.corner=(preferences.corner+1)%4;changed();});
            button("Text / narrated map",r[4],()->Minecraft.getInstance().gui.setScreen(new AtlasDetailsScreen(this,-1)));
            button("Back",r[5],this::onClose);
        }
        @Override public Component getNarrationMessage(){return Component.literal((AtlasLayout.utilitySupported(width,height)?"Atlas HUD settings. ":"View requires at least 240 by 150 GUI units. Lower GUI Scale or enlarge the window. ")+"Visibility, text mode, size, and corner are saved locally. Controls settings also offers rebindable Toggle Atlas HUD and Describe Atlas map keys. "+preferenceNotice);}
        @Override public void extractRenderState(GuiGraphicsExtractor g,int x,int y,float delta){background(g);super.extractRenderState(g,x,y,delta);if(drawTinyFallback(g))return;wrapped(g,preferenceNotice.isEmpty()?"Saved locally. More shortcuts in Controls.":preferenceNotice,12,112,width-24,Math.max(1,(height-144)/11));}
    }
    static final class AtlasDetailsScreen extends AtlasUtilityScreen {
        final int index;
        AtlasDetailsScreen(Screen parent,int index){super("Atlas text map",parent);this.index=index;}
        java.util.List<AtlasModel.Point> points(){
            if(model.snapshot==null)return java.util.List.of();
            var result=new java.util.ArrayList<AtlasModel.Point>(model.snapshot.anchors());result.addAll(model.snapshot.markers());result.addAll(model.players(System.nanoTime()));return result;
        }
        String current(){if(index<0)return describePlayer();var points=points();var c=Minecraft.getInstance();if(points.isEmpty()||c.player==null)return "No nearby points available. Open Atlas and Refresh.";
            var p=points.get(Math.min(index,points.size()-1));String kind=model.snapshot.anchors().contains(p)?"Lodestone: ":model.players(System.nanoTime()).contains(p)?"Player: ":"Marker: ";
            return kind+AtlasModel.describePoint(p,c.player.getX(),c.player.getZ());
        }
        @Override public Component getNarrationMessage(){return Component.literal((AtlasLayout.utilitySupported(width,height)?"Atlas text map. ":"View requires at least 240 by 150 GUI units. Lower GUI Scale or enlarge the window. ")+model.freshness(System.nanoTime())+". "+describePlayer()+" "+AtlasModel.pointPageLabel(index,points().size())+". "+current()+". Use Previous and Next to read every point.");}
        void navigate(int delta){
            int nextIndex=Math.clamp(index+delta,-1,Math.max(-1,points().size()-1));
            var next=new AtlasDetailsScreen(parent,nextIndex);Minecraft.getInstance().gui.setScreen(next);
            int focus=delta<0?0:1;
            if(next.children().get(focus) instanceof Button b&&!b.active)focus=next.children().get(1-focus) instanceof Button other&&other.active?1-focus:2;
            next.setFocused(next.children().get(focus));
        }
        void rebuild(){
            int focus=children().indexOf(getFocused());clearWidgets();init();
            if(focus>=0&&focus<children().size()&&children().get(focus) instanceof Button b&&b.active)setFocused(b);
            else if(children().size()>2)setFocused(children().get(2));
            scheduleNarration();
        }
        @Override protected void init(){
            if(tinyFallback())return;
            var r=AtlasLayout.utilityControls(width,height,false);
            button("Previous page",r[0],()->navigate(-1)).active=index>=0;
            button("Next point",r[1],()->navigate(1)).active=index+1<points().size();
            button("Refresh map",r[2],()->{refreshMap(AtlasRefreshPolicy.Event.MANUAL);scheduleNarration();});
            button("Back",r[3],this::onClose);
        }
        @Override public void extractRenderState(GuiGraphicsExtractor g,int x,int y,float delta){
            background(g);super.extractRenderState(g,x,y,delta);if(drawTinyFallback(g))return;
            wrapped(g,model.freshness(System.nanoTime()),12,28,width-24,1);
            wrapped(g,AtlasModel.pointPageLabel(index,points().size())+". "+current(),12,40,width-24,Math.max(1,(height-86)/11));
        }
    }
    static final class AtlasScreen extends Screen {
        AtlasLayout layout;Button quote,confirm,zoomOut,zoomIn,recenter;int page;
        boolean draggingMap;long lastPanRequest;
        final java.util.Map<Button,String> focusKeys=new java.util.IdentityHashMap<>();
        AtlasScreen(){super(Component.literal("Saturn Atlas"));}
        @Override public Component getNarrationMessage(){
            String summary="Saturn Atlas. Tab or Shift Tab to navigate; Enter to activate; Escape to close. ";
            if(layout!=null&&layout.compact())summary+="Compact destination list. ";
            summary+=model.freshness(System.nanoTime())+". "+describePlayer();
            if(model.state!=AtlasModel.State.READY)summary+=model.state==AtlasModel.State.WAITING?"Requesting shared map. ":model.notice+". ";
            if(model.snapshot!=null)summary+="Map view "+(144*model.zoom)+" blocks across. "+model.snapshot.anchors().size()+" accessible lodestones. Balance "+model.snapshot.balance()+" shards. "+model.travelHint(System.nanoTime());
            return Component.literal(summary);
        }
        @Override public boolean keyPressed(KeyEvent event){if(openKey!=null&&openKey.matches(event)){onClose();return true;}return super.keyPressed(event);}
        void rebuild(){
            String focus=getFocused() instanceof Button b?focusKeys.get(b):null;
            clearWidgets();focusKeys.clear();init();scheduleNarration();
            if(focus!=null)for(var child:children())if(child instanceof Button b&&b.active&&focus.equals(focusKeys.get(b))){setFocused(b);break;}
        }
        Button add(String label,AtlasLayout.Rect r,Runnable action){return add(label,label,r,action);}
        Button add(String label,String description,AtlasLayout.Rect r,Runnable action){
            var button=Button.builder(Component.literal(label),b->action.run()).bounds(r.x(),r.y(),r.w(),r.h()).createNarration(defaultNarration->Component.literal(description)).build();
            focusKeys.put(button,description.replaceFirst("^(Selected|Destination): ",""));
            button.setTooltip(Tooltip.create(Component.literal(description)));return addRenderableWidget(button);
        }
        @Override protected void init(){
            focusKeys.clear();quote=null;confirm=null;layout=AtlasLayout.of(width,height);
            add("Refresh",layout.refresh(),()->refreshMap(AtlasRefreshPolicy.Event.MANUAL));
            zoomOut=zoomIn=recenter=null;
            if(!layout.compact()){
                recenter=add("Center","Center the map on you again",layout.recenter(),()->{model.recenter();rebuild();});
                zoomOut=add("-","Zoom out: show a wider area",layout.zoomOut(),()->zoom(1));
                zoomIn=add("+","Zoom in: show a closer area",layout.zoomIn(),()->zoom(-1));
            }

            add("Text","Text and narrated map: position, heading, all nearby lodestones and markers",new AtlasLayout.Rect(width-108,7,46,18),()->Minecraft.getInstance().gui.setScreen(new AtlasDetailsScreen(this,-1)));
            add("HUD","HUD visibility, text mode, size and placement",new AtlasLayout.Rect(width-58,7,46,18),()->Minecraft.getInstance().gui.setScreen(new AtlasOptionsScreen(this)));
            if(!layout.compact())add("Held Atlas","View the map stored in your held atlas item",layout.held(),()->request("atlas view"));
            quote=add("Get quote","Request the server price for the selected lodestone",layout.quote(),()->request("lodestone quote "+model.selected));
            confirm=add("Travel","Travel requires an unexpired quote and enough shards",layout.confirm(),()->request("lodestone confirm"));
            if(model.snapshot!=null){
                boolean inline=layout.compact()&&height<160;
                int rows=Math.max(1,(layout.sidebar().h()-24)/23);var anchors=model.snapshot.anchors();
                int pages=Math.max(1,(anchors.size()+rows-1)/rows);page=Math.min(page,pages-1);
                for(int i=page*rows;i<Math.min(anchors.size(),(page+1)*rows);i++){
                    var a=anchors.get(i);String label=(a.id().equals(model.selected)?"> ":"")+a.name();int row=i-page*rows;
                    add(font.plainSubstrByWidth(label,layout.sidebar().w()-(inline?116:12)), (a.id().equals(model.selected)?"Selected: ":"Destination: ")+a.name()+" ("+(int)a.x()+", "+(int)a.z()+")",new AtlasLayout.Rect(layout.sidebar().x()+(inline?52:0),layout.sidebar().y()+row*23,layout.sidebar().w()-(inline?104:0),20),()->{model.selected=a.id();rebuild();});
                }
                if(pages>1){int y=inline?layout.sidebar().y():layout.sidebar().bottom()-20;add("Prev","Previous destinations",new AtlasLayout.Rect(layout.sidebar().x(),y,46,20),()->{page=Math.max(0,page-1);rebuild();});add("Next","Next destinations",new AtlasLayout.Rect(layout.sidebar().right()-46,y,46,20),()->{page=Math.min(pages-1,page+1);rebuild();});}
            }
            add("Close",layout.close(),this::onClose);
            controls();
        }
        void zoom(int direction){if(model.zoomBy(direction))rebuild();}
        @Override public boolean mouseScrolled(double mouseX,double mouseY,double scrollX,double scrollY){
            if(layout!=null&&!layout.compact()&&scrollY!=0&&mouseX>=layout.map().x()&&mouseX<layout.map().right()&&mouseY>=layout.map().y()&&mouseY<layout.map().bottom()){zoom(scrollY<0?1:-1);return true;}
            return super.mouseScrolled(mouseX,mouseY,scrollX,scrollY);
        }
        boolean overMap(double x,double y){return layout!=null&&!layout.compact()&&x>=layout.map().x()&&x<layout.map().right()&&y>=layout.map().y()&&y<layout.map().bottom();}
        /** Drag with the left button to look at another part of the shared map. */
        @Override public boolean mouseClicked(MouseButtonEvent event,boolean doubleClick){
            if(super.mouseClicked(event,doubleClick))return true;
            if(event.button()==0&&overMap(event.x(),event.y())&&model.canPan()){draggingMap=true;return true;}
            return false;
        }
        @Override public boolean mouseDragged(MouseButtonEvent event,double dx,double dy){
            if(!draggingMap||model.snapshot==null)return super.mouseDragged(event,dx,dy);
            double scale=AtlasViewport.centered(0,0,layout.map().w(),layout.map().h(),model.zoom).scale();
            if(model.panBy(-dx/scale,-dy/scale)){
                controls();
            }
            return true;
        }
        @Override public boolean mouseReleased(MouseButtonEvent event){
            if(draggingMap&&event.button()==0){draggingMap=false;return true;}
            return super.mouseReleased(event);
        }
        /** Closing only changes local view state; it never sends a sync command. */
        @Override public void removed(){super.removed();model.recenter();}
        void controls(){
            if(recenter!=null){recenter.active=model.panned;recenter.setTooltip(Tooltip.create(Component.literal(!model.canPan()&&model.snapshot!=null?"This server does not support dragging the map yet":model.panned?"Center the map on you again":"Drag the map to look around; this brings it back to you")));}
            if(zoomOut!=null){zoomOut.active=model.canZoomOut();zoomOut.setTooltip(Tooltip.create(Component.literal(model.maxZoom()==1?"This server does not offer wider map views yet":model.canZoomOut()?"Zoom out: show a wider area (or scroll down over the map)":"Widest view")));}
            if(zoomIn!=null){zoomIn.active=model.canZoomIn();zoomIn.setTooltip(Tooltip.create(Component.literal(model.canZoomIn()?"Zoom in: show a closer area (or scroll up over the map)":"Closest view")));}
            if(quote!=null)quote.active=model.canQuote();if(confirm!=null){confirm.active=model.canConfirm(System.nanoTime());confirm.setTooltip(Tooltip.create(Component.literal(model.travelHint(System.nanoTime()))));}}
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
            g.fill(0,0,width,height,0xff111b28);g.fill(8,6,width-8,26,0xff1c3544);super.extractRenderState(g,mouseX,mouseY,delta);
            g.text(font,font.plainSubstrByWidth("ATLAS | "+openHint()+" / Esc: Close | Tab",Math.max(0,width-(layout.compact()?130:188))),14,12,0xffd8f9ff,true);
            if(layout.compact()){
                g.text(font,"LODESTONES (compact list)",12,27,0xffd8f9ff,false);
                g.text(font,font.plainSubstrByWidth(model.freshness(System.nanoTime())+" | "+model.travelHint(System.nanoTime()),width-24),12,height-76,0xffffcf80,false);
                g.text(font,"Balance: "+(model.snapshot==null?"?":model.snapshot.balance())+" shards",12,height-64,0xffedf5fa,false);
                if(model.snapshot==null||model.snapshot.anchors().isEmpty())lines(g,model.snapshot==null?"No map data. Refresh to retry.":"No accessible lodestones.",12,40,width-24,2,0xffedf5fa);
                if(model.snapshot!=null)drawPageCount(g,model.snapshot);return;
            }
            var map=layout.map();int left=map.x(),top=map.y(),size=map.w();
            g.fill(left-1,top-1,map.right()+1,map.bottom()+1,0xff53737f);g.fill(left,top,map.right(),map.bottom(),0xff172735);
            g.text(font,"LODESTONES",layout.sidebar().x(),27,0xffa8edf0,false);
            if(model.snapshot!=null){
                var s=model.snapshot;var view=model.panned?AtlasViewport.centered(model.viewX,model.viewZ,map.w(),map.h(),model.zoom):AtlasViewport.of(s.x(),s.z(),map.w(),map.h(),model.zoom);int step=4*model.dataZoom(model.zoom);
                double startX=view.startX(),startZ=view.startZ(),scale=view.scale();
                g.enableScissor(left,top,map.right(),map.bottom());
                for(var t:model.tilesAtZoom(model.zoom)){if((t.x()+1)*4.0*step<startX||t.x()*4.0*step>startX+map.w()/scale||(t.z()+1)*4.0*step<startZ||t.z()*4.0*step>startZ+map.h()/scale)continue;for(int i=0;i<16;i++){if(t.pixels()[i]==0)continue;int px=(int)Math.floor(((t.x()*4+i%4)*step-startX)*scale),py=(int)Math.floor(((t.z()*4+i/4)*step-startZ)*scale);g.fill(left+px,top+py,Math.min(map.right(),left+px+(int)Math.ceil(step*scale)),Math.min(map.bottom(),top+py+(int)Math.ceil(step*scale)),color(t.pixels()[i]));}}
                for(var p:s.markers())point(g,p,left,top,startX,startZ,scale,map.w(),map.h(),0xffffd56a);
                for(var p:model.players(System.nanoTime()))otherPlayer(g,p,left,top,startX,startZ,scale,map.w(),map.h());
                for(var p:s.anchors())point(g,p,left,top,startX,startZ,scale,map.w(),map.h(),0xff8aeeff);
                int px=left+(int)((s.x()-startX)*scale),py=top+(int)((s.z()-startZ)*scale);if(px>=-5&&py>=-5&&px<map.w()+left+5&&py<map.h()+top+5)player(g,px,py);
                g.disableScissor();
                g.fill(map.right()-14,top+3,map.right()-3,top+15,0xee111b28);
                g.text(font,"N",map.right()-11,top+5,0xffffffff,true);
                String span="View: "+(144*model.zoom)+" blocks"+(model.panned?" | centre "+(int)Math.floor(model.viewX)+", "+(int)Math.floor(model.viewZ):model.canPan()?" | drag to look around":"");g.fill(left+3,top+3,left+9+font.width(span),top+15,0xee111b28);g.text(font,span,left+6,top+5,0xffedf5fa,true);
                g.fill(left+3,map.bottom()-14,map.right()-3,map.bottom()-2,0xee111b28);
                g.text(font,font.plainSubstrByWidth("+ You   Green: players   [] Stone   <> Marker",size-12),left+6,map.bottom()-12,0xffedf5fa,true);
                if(s.anchors().isEmpty())lines(g,"No lodestones yet. Place a lodestone and it appears here for everyone; right-click it to rename.",layout.sidebar().x(),42,layout.sidebar().w(),4,0xffbac7d5);
                drawPageCount(g,s);
                String cost=model.travelHint(System.nanoTime());
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
        void drawPageCount(GuiGraphicsExtractor g,AtlasModel.Snapshot s){
            int rows=Math.max(1,(layout.sidebar().h()-24)/23);
            if(!(layout.compact()&&height<160)&&s.anchors().size()>rows)g.text(font,(page+1)+"/"+((s.anchors().size()+rows-1)/rows),layout.sidebar().x()+54,layout.sidebar().bottom()-14,0xffedf5fa,false);
        }
        static void otherPlayer(GuiGraphicsExtractor g,AtlasModel.Point p,int left,int top,double sx,double sz,double scale,int w,int h){
            int x=(int)Math.floor((p.x()-sx)*scale),y=(int)Math.floor((p.z()-sz)*scale);if(x<4||y<4||x>=w-4||y>=h-4)return;
            g.fill(left+x-4,top+y-4,left+x+5,top+y+5,0xff111b28);g.fill(left+x-2,top+y-2,left+x+3,top+y+3,0xff8cff9e);
            var font=Minecraft.getInstance().font;String name=font.plainSubstrByWidth(p.name(),Math.max(0,w-x-6));if(!name.isEmpty())g.text(font,name,left+x+6,top+y-3,0xff8cff9e,true);
        }
        static void player(GuiGraphicsExtractor g,int x,int y){g.fill(x-4,y-1,x+5,y+2,0xff111b28);g.fill(x-1,y-4,x+2,y+5,0xff111b28);g.fill(x-3,y,x+4,y+1,0xffffffff);g.fill(x,y-3,x+1,y+4,0xffffffff);}
        static int color(int p){return switch(p){case 6->0xff77a84d;case 10->0xffd9ce92;case 34->0xffe5eff3;case 42->0xff8a6849;case 50->0xff466dae;case 18->0xffd16835;default->0xff8b8d91;};}
        static void point(GuiGraphicsExtractor g,AtlasModel.Point p,int left,int top,double sx,double sz,double scale,int mapWidth,int mapHeight,int color){
            int px=(int)((p.x()-sx)*scale),py=(int)((p.z()-sz)*scale);if(px<3||py<3||px>=mapWidth-3||py>=mapHeight-3)return;
            g.fill(left+px-4,top+py-4,left+px+5,top+py+5,0xff111b28);
            for(int d=-3;d<=3;d++){int e=color==0xff8aeeff?3:3-Math.abs(d);g.fill(left+px+d,top+py-e,left+px+d+1,top+py-e+1,color);g.fill(left+px+d,top+py+e,left+px+d+1,top+py+e+1,color);}
            if(color==0xff8aeeff){g.fill(left+px-3,top+py-3,left+px-2,top+py+4,color);g.fill(left+px+3,top+py-3,left+px+4,top+py+4,color);}
            g.fill(left+px,top+py,left+px+1,top+py+1,0xffffffff);
        }
        @Override public boolean isPauseScreen(){return false;}
    }
}
