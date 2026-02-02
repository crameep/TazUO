using System;
using System.Collections.Concurrent;
using System.Threading;

namespace ClassicUO.Game.Managers;

public static class MainThreadQueue
{
    private static int _threadId;
    private static bool _isMainThread => Thread.CurrentThread.ManagedThreadId == _threadId;
    private static ConcurrentQueue<Action> _queuedActions { get; } = new();

    /// <summary>
    /// Must be called from main thread
    /// </summary>
    public static void Load() => _threadId = Thread.CurrentThread.ManagedThreadId;

    /// <summary>
    /// This will not wait for the action to complete.
    /// </summary>
    /// <param name="action"></param>
    public static void EnqueueAction(Action action) => _queuedActions.Enqueue(action);

    /// <summary>
    /// Wraps the given function with a try/catch, returning any caught exception
    /// </summary>
    /// <param name="callback">The function to wrap</param>
    /// <typeparam name="T"></typeparam>
    /// <returns>A tuple of the Result and Exception (if one occurred)</returns>
    private static Func<(T, Exception)> WrapCallback<T>(Func<T> callback) =>
        () =>
        {
            try
            {
                return (callback(), null);
            }
            catch (Exception e)
            {
                return (default, e);
            }
        };

    /// <summary>
    /// Dispatches the given function for invocation on the main thread and waits synchronously for the result
    /// </summary>
    /// <param name="func">The function to invoke on the main thread</param>
    /// <typeparam name="T"></typeparam>
    /// <returns>The result of the function's invocation</returns>
    /// <exception cref="Exception">The exception, if any, raised by the function invocation</exception>
    private static T BubblingDispatchToMainThread<T>(Func<T> func)
    {
        var resultEvent = new ManualResetEvent(false);

        T mtResult = default;
        Exception ex = null;

        _queuedActions.Enqueue(MtAction);
        resultEvent.WaitOne(); // Wait for the main thread to complete the operation

        return ex != null ? throw ex : mtResult;

        void MtAction()
        {
            var (res, e) = WrapCallback(func)();
            mtResult = res;
            ex = e;
            resultEvent.Set();
        }
    }

    /// <summary>
    /// Dispatches a given function for execution on the MainThread.
    ///
    /// If the current thread is the main thread, the function will run immediately as-is,
    /// otherwise, the function will be dispatched and waited for.
    /// Any exceptions raised on the main thread's context will be captured and bubbled back.
    /// </summary>
    /// <param name="func">The function to execute</param>
    /// <typeparam name="T"></typeparam>
    /// <returns>The function's result</returns>
    /// <exception cref="Exception">On any exception thrown by the given function</exception>
    public static T BubblingInvokeOnMainThread<T>(Func<T> func) =>
        _isMainThread
            ? func()
            : BubblingDispatchToMainThread(func);

    /// <summary>
    /// This will wait for the returned result.
    /// </summary>
    /// <param name="func"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T InvokeOnMainThread<T>(Func<T> func)
    {
        if (_isMainThread) return func();

        var resultEvent = new ManualResetEvent(false);
        T result = default;

        void action()
        {
            result = func();
            resultEvent.Set();
        }

        _queuedActions.Enqueue(action);
        resultEvent.WaitOne(); // Wait for the main thread to complete the operation

        return result;
    }

    /// <summary>
    /// This will not wait for the returned result.
    /// </summary>
    /// <param name="action"></param>
    public static void InvokeOnMainThread(Action action)
    {
        if (_isMainThread)
        {
            action();
            return;
        }

        _queuedActions.Enqueue(action);
    }

    /// <summary>
    /// Must only be called on the main thread
    /// </summary>
    public static void ProcessQueue()
    {
        while (_queuedActions.TryDequeue(out Action action))
        {
            action();
        }
    }

    public static void Reset()
    {
        while (_queuedActions.TryDequeue(out _)) { }
    }
}
