using System.Diagnostics;
using System.Security.Cryptography;

namespace HW_Async_Thread;

public class Program
{
    public static void Main()
    {
        int cnt = 0;
        while (true)
        {
            // 测试用例: (a + b) * (c + d)
            Console.WriteLine($"=={cnt++}==");
            ValueExpr a = new(1);
            ValueExpr b = new(2);
            ValueExpr c = new(3);
            ValueExpr d = new(4);
            AddExpr add1 = new(a, b);
            AddExpr add2 = new(c, d);
            MulExpr mul = new(add1, add2);
            mul.WaitForAvailable();
            Console.WriteLine(mul.Val);
            Debug.Assert(mul.Val == 21);
            //Console.WriteLine(mul.modifiedCount);
            //Thread.Sleep(1);
            a.NewVal = 5;
            mul.WaitForAvailable();
            Console.WriteLine(mul.Val);
            Debug.Assert(mul.Val == 49);
            //Console.WriteLine(mul.modifiedCount);
        }
    }
}

/// <summary>
/// 表达式结点抽象类，用于构造表达式树
/// </summary>
public abstract class Expr
{
    /// <summary>
    /// 父结点
    /// </summary>
    protected Expr? parent = null;

    /// <summary>
    /// 表达式的值，只允许返回一个现成的值，可以加锁
    /// 拓展思考：是否所有时候读取到的值都是其正确的值？如何避免？
    /// </summary>
    public abstract int Val { get; }

    /// <summary>
    /// 结点值是否可用，用于判断结点值是否已经更新完毕
    /// </summary>
    /// <value></value>
    public bool Available => modifiedCount <= 0;
    // 需要等待的更新计数
    public int modifiedCount = 0;
    private object availableLock = new();

    public void WaitForAvailable()
    {
        if (!Available)
            lock (availableLock)
                if (!Monitor.Wait(availableLock, 1000)) Console.Write("没等到");
        modifiedCount = 0;
    }

    /// <summary>
    /// 异步方法，它的作用是启动一个任务，推动结点自身及其父结点更新值
    /// 可以根据自身需求适当修改方法签名
    /// </summary>
    public abstract Task Update(string? from = null);

    /// <summary>
    /// 注册父结点
    /// 思考：当父结点被注册后，父结点的值是否需要更新？
    /// </summary>
    /// <param name="parent">待注册的父结点</param>
    public abstract void Register(Expr parent);

    protected static void SetParent(Expr child, Expr parent)
    {
        child.parent = parent;
    }

    public bool Unavailablize(string? from = null, bool recursively = true)
    {
        from ??= ToString();
        //if (this is BinaryExpr)
            //Console.WriteLine($"{this} Unavailable from {from}");
        //lock (availableLock)
        modifiedCount++;
        if (recursively && parent is not null)
        {
            parent.Unavailablize(from);
            return true;
        }
        return false;
    }
    protected void Availablize(string? from = null)
    {
        //if (this is BinaryExpr)
            //Console.WriteLine($"{this} Available from {from}");
        if (!Available)
            //lock (availableLock)
            modifiedCount--;
        if (Available)
        {
            lock (availableLock)
                Monitor.PulseAll(availableLock);
        }

    }
}

/// <summary>
/// 数据结点
/// </summary>
/// <param name="initVal">初始值</param>
public class ValueExpr(int initVal) : Expr
{
    int val = initVal;
    readonly object lockVal = new();
    public override int Val
    {
        get
        {
            lock (lockVal)
                return val;
        }
    }

    /// <summary>
    /// 修改数据
    /// 思考：修改数据后，父结点是否也需要更新？
    /// </summary>
    public int NewVal
    {
        set
        {
            Unavailablize();
            lock (lockVal)
            {
                val = value;
            }
            Availablize();
            parent?.Update();
        }
    }

    public override async Task Update(string? from = null)
    {
        from ??= ToString();
        Availablize(from);
        if (parent != null)
            await parent.Update(from);
    }

    public override void Register(Expr parent)
    {
        this.parent = parent;
        parent.Update();
    }

    public override string ToString()
    {
        return val.ToString();
    }
}

/// <summary>
/// 二元运算表达式结点
/// </summary>
public class BinaryExpr : Expr
{
    int val = 0;
    readonly object lockVal = new();
    public override int Val
    {
        get
        {
            lock (lockVal)
                return val;
        }
    }

    public Expr ExprA, ExprB;
    public Func<int, int, int> ExprFunc { get; private set; }
    public BinaryExpr(Expr A, Expr B, Func<int, int, int> func)
    {
        ExprA = A;
        ExprB = B;
        ExprFunc = func;
        Unavailablize();
        SetParent(A, this);
        if (!A.Available) Unavailablize();
        SetParent(B, this);
        if (!B.Available) Unavailablize();
        _ = Update();
        //_ = InitialUpdate();

    }


    private async Task InitialUpdate()
    {
        bool UnavailablizeParent = !Unavailablize();
        await Task.Run(() =>
        {
            //Unavailablize();
            val = ExprFunc(ExprA.Val, ExprB.Val);
            Availablize(ToString());
        });
        if (parent != null)
        {
            if (UnavailablizeParent)
                parent.Unavailablize();
            await parent.Update();
        }
    }
    public override async Task Update(string? from = null)
    {        
        from ??= ToString();
        // 避免阻塞主线程   
        await Task.Run(() =>
        {
            //Unavailablize();
            val = ExprFunc(ExprA.Val, ExprB.Val);
            Availablize(from);
        });
        if (parent != null)
            await parent.Update(from);
    }

    public override void Register(Expr parent)
    {
        parent.Unavailablize();
        this.parent = parent;
        parent.Update();
    }
}

/// <summary>
/// 加法运算表达式结点
/// </summary>
public class AddExpr(Expr A, Expr B) : BinaryExpr(A, B, (a, b) =>
{
    // Thread.Sleep(10);
    return a + b;
})
{
    public override string ToString()
    {
        return $"{A}+{B}";
    }
}

/// <summary>
/// 乘法运算表达式结点
/// </summary>
public class MulExpr(Expr A, Expr B) : BinaryExpr(A, B, (a, b) =>
{
    // Thread.Sleep(10);
    return a * b;
})
{
    public override string ToString()
    {
        return $"({A})*({B})";
    }
}
