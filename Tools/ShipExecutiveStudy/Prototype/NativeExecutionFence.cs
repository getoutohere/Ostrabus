using System;
using System.Threading;

namespace ShipExecutivePrototype;

// Offline proposal for the synchronous native capture boundary. It coordinates
// main-thread callbacks, not Unity access from a worker or a second simulation.
sealed class NativeExecutionFence : IDisposable
{
    internal static NativeExecutionFence Current;
    readonly int owner = Thread.CurrentThread.ManagedThreadId;
    int mutations;
    bool capturing;
    internal NativeExecutionFence()
    {
        if (Current != null)
            throw new InvalidOperationException("Nested execution coordinator");
        Current = this;
    }
    void CheckThread()
    {
        if (!ReferenceEquals(Current, this))
            throw new InvalidOperationException("Execution coordinator is not active");
        if (Thread.CurrentThread.ManagedThreadId != owner)
            throw new InvalidOperationException("Native world capture/mutation requires its owner thread");
    }
    internal void CertifyInvocationBoundary()
    {
        CheckThread();
        if (capturing || mutations == 0)
            throw new InvalidOperationException("Native invocation is outside its coordinated operation");
    }
    internal IDisposable Capture()
    {
        CheckThread();
        if (capturing || mutations != 0)
            throw new InvalidOperationException("Defer save until the native operation finishes");
        capturing = true;
        return new Scope(() => capturing = false);
    }
    internal static IDisposable Mutation()
    {
        var fence = Current;
        if (fence == null)
            return null;
        fence.CheckThread();
        if (fence.capturing)
            throw new InvalidOperationException("World mutation during save capture");
        fence.mutations++;
        return new Scope(() => fence.mutations--);
    }
    sealed class Scope : IDisposable
    {
        Action release;
        internal Scope(Action release)
        {
            this.release = release;
        }
        public void Dispose()
        {
            var once = release;
            release = null;
            once?.Invoke();
        }
    }
    public void Dispose()
    {
        if (capturing || mutations != 0)
            throw new InvalidOperationException("Coordinator still has an active operation");
        Current = null;
    }
}
