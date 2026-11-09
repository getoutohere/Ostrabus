using System.Text.Json;

namespace Ostrabus.TorchCalculator;

static class BrowserChecks {
    internal static void Export(string output) {
        var fits = new List<ReactorFit>();
        foreach (int pairs in new[] { 1, 2, 3, 4 }) foreach (double mass in new[] { 16000d, 32000, 100000 })
            foreach (bool safety in new[] { false, true }) foreach (double health in new[] { 1d, .6 })
                fits.Add(new ReactorFit { Feeders = pairs, Lasers = pairs, MassKg = mass, Safety = safety,
                    FeederHealth = health, LaserHealth = health, Capacitors = 2, Regulators = 2, SkilledPilot = safety });
        fits.Add(new ReactorFit { Feeders = 0 }); fits.Add(new ReactorFit { Regulators = 0 });
        fits.Add(new ReactorFit { Capacitors = 0 }); fits.Add(new ReactorFit { Cryopumps = 0 });
        fits.Add(new ReactorFit { Cryopumps = 3, CryoHealth = .4, Feeders = 4, Lasers = 4 });
        fits.Add(new ReactorFit { DesiredG = 100, MassKg = 40000, BurnHours = .2 });
        fits.Add(new ReactorFit { He3Kg = TorchModel.He3TankKg, D2OKg = TorchModel.D2OTankKg });
        fits.Add(new ReactorFit { He3Kg = TorchModel.He3TankKg * 2, D2OKg = TorchModel.D2OTankKg, Feeders = 2, Lasers = 2 });
        var cases = fits.Select(fit => new { fit, expected = TorchModel.Evaluate(fit) });
        string json = JsonSerializer.Serialize(cases, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        string html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "calculator.html"));
        string test = "<pre id=\"browser-checks\">Running browser checks...</pre><script>\nconst nativeCases=" + json + ";\n" + """
            (()=>{
                const output=document.getElementById('browser-checks');let checks=0;
                function check(ok,message){checks++;if(!ok)throw Error(message)}
                function compare(actual,expected,path){
                    if(typeof expected==='number')check(Number.isFinite(actual)&&Math.abs(actual-expected)<=3e-6*Math.max(1e-7,Math.abs(expected)),path+': '+actual+' != '+expected);
                    else if(expected===null||typeof expected!=='object')check(actual===expected,path+' differs');
                    else if(Array.isArray(expected)){check(Array.isArray(actual)&&actual.length===expected.length,path+' length differs');expected.forEach((e,i)=>compare(actual[i],e,path+'['+i+']'))}
                    else for(const key of Object.keys(expected))compare(actual[key],expected[key],path+'.'+key);
                }
                try{
                    check(location.protocol==='file:','Must test direct filesystem opening');
                    check(document.getElementById('error').textContent==='','Initial calculation failed');
                    check(document.getElementById('max').textContent.includes('3.31'),'Default maximum did not render');
                    check(document.querySelectorAll('#compare tr').length===4,'Comparison table did not render');
                    check(document.getElementById('fuelAvailable').textContent.includes('5,216'),'Default full He-3 tank availability not shown');
                    check(document.getElementById('he3').disabled&&!document.getElementById('heTanks').disabled,'Tank mode input validation incorrect');
                    nativeCases.forEach((c,i)=>compare(TorchModel.evaluate(c.fit),c.expected,'case '+i));
                    for(const massKg of [0,-1,NaN,Infinity]){let rejected=false;try{TorchModel.evaluate({massKg})}catch{rejected=true}check(rejected,'Invalid mass accepted')}
                    document.getElementById('pairs').value='2';document.getElementById('pairs').dispatchEvent(new Event('change',{bubbles:true}));
                    check(document.getElementById('heRate').textContent.includes('797.2'),'Pair control did not update browser calculation');
                    document.getElementById('safety').checked=true;document.getElementById('safety').dispatchEvent(new Event('change',{bubbles:true}));
                    check(document.getElementById('requested').textContent.includes('over limit'),'Safety toggle did not apply');
                    check(document.getElementById('compare').textContent.includes('Safety limit'),'Safety mass comparison is misleading');
                    document.getElementById('safety').checked=false;update();
                    const oldMass=Number(document.getElementById('mass').value);document.getElementById('addFuel').click();
                    check(Number(document.getElementById('mass').value)===oldMass+1500,'Fuel button must add one tank hardware mass only');
                    check(Number(document.getElementById('heTanks').value)===2&&Number(document.getElementById('dTanks').value)===1,'Full-tank counts did not update');
                    check(document.getElementById('error').textContent==='','Tank mode calculation failed');
                    const tankMass=Number(document.getElementById('mass').value);
                    document.getElementById('heTanks').value='3';document.getElementById('heTanks').dispatchEvent(new Event('change',{bubbles:true}));
                    check(Number(document.getElementById('mass').value)===tankMass,'Existing tank input must not add hardware mass again');
                    document.getElementById('fuelMode').value='kg';document.getElementById('fuelMode').dispatchEvent(new Event('change',{bubbles:true}));
                    check(!document.getElementById('he3').disabled&&document.getElementById('heTanks').disabled,'KG mode input validation incorrect');
                    const kgMass=Number(document.getElementById('mass').value);document.getElementById('addFuel').click();
                    check(Number(document.getElementById('mass').value)===kgMass+4500,'KG mode must add only three tank hardware masses');
                    check(Number(document.getElementById('he3').value)===2000+2*5216&&Number(document.getElementById('d2o').value)===2000+44722.8,'KG mode must add full tank contents');
                    check(document.getElementById('error').textContent==='','Updated KG/mass values failed validation');
                    document.getElementById('feeders').value='0';document.getElementById('feeders').dispatchEvent(new Event('change',{bubbles:true}));
                    check(document.getElementById('max').textContent.trim()==='0 G','Missing feeder state did not render');
                    output.dataset.status='passed';output.textContent=checks+' browser comparisons and UI checks passed across '+nativeCases.length+' C# reference scenarios.';
                }catch(error){output.dataset.status='failed';output.textContent='FAILED: '+error.stack}
            })();
            </script>
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, html.Replace("</body>", test + "</body>"));
        Console.WriteLine("Exported filesystem browser checks: " + Path.GetFullPath(output));
    }
}
