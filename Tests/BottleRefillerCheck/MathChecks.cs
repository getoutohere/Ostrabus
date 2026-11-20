using System;
using System.Linq;
using Ostrabus.BottleRefiller;

internal static partial class Program
{
    private static void MathCases()
    {
        var random=new Random(39971);
        for(int trial=0;trial<2000;trial++)
        {
            var source=new GasState(new[]{random.NextDouble()*100,random.NextDouble()*50,random.NextDouble()},50+random.NextDouble()*900,1);
            var bottles=Enumerable.Range(0,1+trial%4).Select(_=>new GasState(new[]{random.NextDouble(),random.NextDouble(),0d},50+random.NextDouble()*900,.003,20684)).ToArray();
            var plan=TransferPlan.Create(source,bottles);
            Check(plan!=null,"Valid mathematical plan rejected");
            for(int s=0;s<source.Species.Length;s++)
            {
                Near(plan.Debits[s],plan.Credits.Sum(c=>c[s]),"Plan species balance");
                Check(plan.Debits[s]<=source.Species[s],"Plan source overdraw");
            }
            for(int b=0;b<bottles.Length;b++)
            {
                double finalMoles=bottles[b].Species.Sum()+plan.Credits[b].Sum();
                double pressure=finalMoles*TransferPlan.R*plan.Temperatures[b]/bottles[b].Volume;
                Check(pressure<=20684,"Mixed-temperature pressure cap violated");
                Near(bottles[b].Species.Sum()*bottles[b].Temperature+plan.Credits[b].Sum()*source.Temperature,
                    finalMoles*plan.Temperatures[b],"Mole-weighted temperature conservation");
                Near(plan.Credits[b][0]/plan.Credits[b].Sum(),source.Species[0]/source.Species.Sum(),"Mixture changed");
            }
        }
        var healthy=new GasState(new[]{0d,0d},293,.003,20684);
        var sourceGas=new GasState(new[]{10d,20d},293,10);
        Check(TransferPlan.Create(sourceGas,new[]{new GasState(new[]{100d,100d},293,.003,20684)})==null,"Overfull bottle gets gas");
        Check(TransferPlan.Create(new GasState(new[]{0d,0d},293,1),new[]{healthy})==null,"Empty supply creates gas");
        Check(TransferPlan.Create(new GasState(new[]{double.NaN,1d},293,1),new[]{healthy})==null,"NaN supply accepted");
        Check(TransferPlan.Create(new GasState(new[]{-1d,10d},293,1),new[]{healthy})==null,"Negative supply accepted");
        Check(TransferPlan.Create(new GasState(new[]{1d,10d},0,1),new[]{healthy})==null,"Zero source temperature accepted");
        Check(TransferPlan.Create(sourceGas,new[]{new GasState(new[]{0d,0d},293,0,20684)})==null,"Zero destination volume accepted");
        Check(TransferPlan.Create(sourceGas,new[]{new GasState(new[]{0d,0d},double.PositiveInfinity,.003,20684)})==null,"Infinite bottle temperature accepted");
        Check(TransferPlan.Create(sourceGas,new[]{healthy,healthy,healthy,healthy,healthy})==null,"Fifth destination admitted");
        var full=new GasState(new[]{20684*.003/TransferPlan.R/293,0d},293,.003,20684);
        var partlyFull=new GasState(new[]{5d,0d},293,.003,20684);
        var limited=new GasState(new[]{.4,.6},293,1);
        var fair=TransferPlan.Create(limited,new[]{healthy,partlyFull,full});
        Check(fair!=null,"Limited-source plan rejected");
        Near(fair.Credits[2].Sum(),0,"Full bottle topped up");
        Near(fair.Credits[0].Sum(),fair.Credits[1].Sum(),"Equal per-tick requests not shared proportionally");
        Near(fair.Total,1,"Limited supply not used");
        var cold=TransferPlan.Create(new GasState(new[]{100d,0d},50,1),new[]{new GasState(new[]{1d,0d},1000,.003,20684)});
        var hot=TransferPlan.Create(new GasState(new[]{100d,0d},1000,1),new[]{new GasState(new[]{1d,0d},50,.003,20684)});
        Check(cold!=null&&hot!=null,"Hot/cold transfer rejected");
        var pureNitrogen=TransferPlan.Create(new GasState(new[]{0d,100d},293,1),new[]{healthy});
        Near(pureNitrogen.Credits[0][0],0,"Pure nitrogen became oxygen");
    }
}
