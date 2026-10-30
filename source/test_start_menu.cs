using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

static class StartMenuChecks {
    static void Check(bool value,string message) {if(!value) throw new Exception(message);}
    static string Property(object shortcut,string name) {
        return (string)shortcut.GetType().InvokeMember(name,BindingFlags.GetProperty,null,shortcut,null);
    }
    [STAThread] static int Main() {
        string fixture=Path.Combine(Path.GetTempPath(),"TokenTaskbar-shortcut-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        string link=Path.Combine(fixture,"Token Timer - TokenTaskbar.lnk");
        string oldTarget=Path.Combine(fixture,@"old\TokenTaskbar.exe"),newTarget=Path.Combine(fixture,@"new\TokenTaskbar.exe");
        object shell=null,shortcut=null;
        try {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            Check(StartMenuShortcut.Ensure(link,oldTarget),"First launch did not create shortcut");
            shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{link});
            Check(Property(shortcut,"TargetPath")==oldTarget && Property(shortcut,"IconLocation")==oldTarget+",0","Wrong target or icon");
            Marshal.FinalReleaseComObject(shortcut);shortcut=null;
            Check(!StartMenuShortcut.Ensure(link,oldTarget),"Unchanged shortcut was rewritten");
            Check(StartMenuShortcut.Ensure(link,newTarget),"Moved application did not repair shortcut");
            shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{link});
            Check(Property(shortcut,"TargetPath")==newTarget && Property(shortcut,"IconLocation")==newTarget+",0"
                && Property(shortcut,"WorkingDirectory")==Path.GetDirectoryName(newTarget) && Property(shortcut,"Arguments")=="","Moved shortcut has incorrect properties");
            var assembly=typeof(StartMenuShortcut).Assembly;
            using(var stream=assembly.GetManifestResourceStream("TokenTaskbar.App.ico")) {
                Check(stream!=null,"Embedded application icon is missing");
                using(var icon=new Icon(stream,32,32)) Check(icon.Width==32 && icon.Height==32,"Wrong icon size");
            }
            using(var icon=Icon.ExtractAssociatedIcon(assembly.Location)) {
                Check(icon!=null,"Executable shell icon is missing");
                using(var bitmap=icon.ToBitmap()) {
                    var orange=bitmap.GetPixel(bitmap.Width/2,bitmap.Height*10/32);
                    Check(orange.R>180 && orange.G>70 && orange.B<150,"Executable is not using the orange/blue app icon");
                }
            }
            Console.WriteLine("PASS: Start menu creation, no-op, relocation repair, shortcut icon and executable icon");
            return 0;
        } catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
        finally {
            if(shortcut!=null) Marshal.FinalReleaseComObject(shortcut);
            if(shell!=null) Marshal.FinalReleaseComObject(shell);
            // Only the generated test shortcut; never the user's Start menu.
            if(File.Exists(link)) File.Delete(link);
        }
    }
}
