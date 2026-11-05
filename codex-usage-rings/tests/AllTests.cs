using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
public static class AllTests
{
    public static int Main(string[] args)
    {
        try {
            int usage=UsageTests.Run();
            int rendering=RenderingTests.Run();
            int enhancements=EnhancementTests.Run();
            int fast=FastModeTests.Run();
            int selection=ModelSelectionTests.Run();
            int gradient=UltraGradientTests.Run();
            int safety=RenderSafetyTests.Run();
            int reset=ResetCardTests.Run();
            int refill=ResetRefillTests.Run();
            int stressFrames=FlowStress.Run();
            if(LocatorTests.Main()!=0) throw new Exception("Locator tests failed");
            object result=new { passed=true, usageAssertions=usage, renderingAssertions=rendering, enhancementAssertions=enhancements, fastModeAssertions=fast, modelSelectionAssertions=selection, ultraGradientAssertions=gradient, renderSafetyAssertions=safety, resetCardAssertions=reset, refillAssertions=refill, stressFrames=stressFrames, locatorCases=12, total=usage+rendering+enhancements+fast+selection+gradient+safety+reset+refill+12, checkedAtUtc=DateTime.UtcNow.ToString("o") };
            string json=new JavaScriptSerializer().Serialize(result);
            Console.WriteLine(json);
            if(args.Length>0) File.WriteAllText(args[0],json,new UTF8Encoding(false));
            return 0;
        } catch(Exception e) { Console.WriteLine("FAIL: "+e.Message); return 1; }
    }
}
