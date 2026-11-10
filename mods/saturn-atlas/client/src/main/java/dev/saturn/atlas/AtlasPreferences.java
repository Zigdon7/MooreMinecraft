package dev.saturn.atlas;

import java.io.*;
import java.nio.file.*;
import java.util.Properties;

/** Local presentation only; never sent to the server. */
public final class AtlasPreferences {
 public boolean visible=true,text=false;
 public int size=1,corner=0;
 public static AtlasPreferences load(Path file){
  var result=new AtlasPreferences();var values=new Properties();
  try(var reader=Files.newBufferedReader(file)){values.load(reader);
   result.visible=Boolean.parseBoolean(values.getProperty("visible","true"));
   result.text=Boolean.parseBoolean(values.getProperty("text","false"));
   result.size=Math.clamp(Integer.parseInt(values.getProperty("size","1")),0,2);
   result.corner=Math.clamp(Integer.parseInt(values.getProperty("corner","0")),0,3);
  }catch(IOException|IllegalArgumentException ignored){/* Missing/invalid preferences use safe defaults. */}
  return result;
 }
 public void save(Path file)throws IOException{
  Files.createDirectories(file.toAbsolutePath().getParent());var values=new Properties();
  values.setProperty("visible",Boolean.toString(visible));values.setProperty("text",Boolean.toString(text));
  values.setProperty("size",Integer.toString(size));values.setProperty("corner",Integer.toString(corner));
  Path temporary=Files.createTempFile(file.toAbsolutePath().getParent(),"atlas-prefs-",".tmp");
  try{try(var writer=Files.newBufferedWriter(temporary)){values.store(writer,"Saturn Atlas local HUD preferences");}
   try{Files.move(temporary,file,StandardCopyOption.ATOMIC_MOVE,StandardCopyOption.REPLACE_EXISTING);}
   catch(AtomicMoveNotSupportedException e){Files.move(temporary,file,StandardCopyOption.REPLACE_EXISTING);}
  }finally{Files.deleteIfExists(temporary);}
 }
 public String cornerName(){return switch(corner){case 1->"Top left";case 2->"Bottom right";case 3->"Bottom left";default->"Top right";};}
}
