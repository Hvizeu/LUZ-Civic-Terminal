namespace NivalisRecovery;

public static class RecoveryTransfer
{
    // Native adapters retain item identity. The journal owner decides whether a
    // failed publication can be rolled back; memory never contradicts disk.
    public static int Run<T>(IReadOnlyList<T> stock,Func<T,bool> fits,Func<T,bool> take,Func<T,bool> add,Func<T,bool> returnToSource,Func<T,bool> removeFromDestination,Action publish)
    {
        var moved=new List<T>();bool attemptedPublication=false;
        try
        {
            foreach(var item in stock)
            {
                if(!fits(item))continue;
                if(!take(item))throw new InvalidOperationException("Recovery item could not leave its source.");
                moved.Add(item); // Own the removed item before the destination operation.
                if(!add(item))
                {
                    if(!returnToSource(item))throw new InvalidOperationException("Rejected recovery transfer could not roll back.");
                    moved.RemoveAt(moved.Count-1);continue;
                }
            }
            if(moved.Count==0)return 0;
            attemptedPublication=true;publish();return moved.Count;
        }
        catch(Exception first)
        {
            var failures=new List<Exception>{first};
            if(!attemptedPublication)foreach(var item in moved.AsEnumerable().Reverse())
                try{if(!removeFromDestination(item)||!returnToSource(item))throw new InvalidOperationException("Recovery transfer rollback unresolved.");}catch(Exception cleanup){failures.Add(cleanup);}
            if(failures.Count!=1)throw new AggregateException("Recovery transfer remains unresolved.",failures);
            throw;
        }
    }
}
