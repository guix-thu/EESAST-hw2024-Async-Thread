namespace HW_Async_Thread;

public class Program
{
    public static void Main()
    {
        // 测试用例: (a + b) + (c + d)
        // 可以自行修改测试用例
        ValueExpr a = new(1);
        ValueExpr b = new(2);
        ValueExpr c = new(3);
        ValueExpr d = new(4);
        AddExpr add1 = new(a, b);
        AddExpr add2 = new(c, d);
        AddExpr add3 = new(add1, add2);
        add3.WaitForAvailable();
        Console.WriteLine(add3.Val);
        a.NewVal = 5;
        add3.WaitForAvailable();
        Console.WriteLine(add3.Val);
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
    public bool Available { get; protected set; } = true;

    public void WaitForAvailable()
    {
        while (!Available) ;
    }

    /// <summary>
    /// 异步方法，它的作用是启动一个任务，推动结点自身及其父结点更新值
    /// 可以根据自身需求适当修改方法签名
    /// </summary>
    public abstract Task Update();

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
    protected void Unavilablize()
    {
        if (Available)
        {
            Available = false;
            parent?.Unavilablize();
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
            {
                return val;
            }
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
            lock (lockVal)
            {
                Unavilablize();
                val = value;
                Available = true;
            }
            parent?.Update();
        }
    }

    public override async Task Update()
    {
        Available = true;
        if (parent != null)
        {
            await parent.Update();
        }
    }

    public override void Register(Expr parent)
    {
        this.parent = parent;
        parent.Update();
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
            {
                return val;
            }
        }
    }

    public Expr ExprA, ExprB;
    public Func<int, int, int> ExprFunc { get; private set; }
    public BinaryExpr(Expr A, Expr B, Func<int, int, int> func)
    {
        ExprA = A;
        ExprB = B;
        ExprFunc = func;
        // A.Register(this);
        // B.Register(this);
        /* 
         * 直接对parent进行修改，而不是调用Register方法
         * Register操作不可能对子树造成影响，只会对父节点及以上的节点值造成影响
         * 而此处构造函数中已经进行了计算，使用Register造成了重复计算，白白浪费两倍时间 
         */
        SetParent(A, this);
        SetParent(B, this);
        val = ExprFunc(ExprA.Val, ExprB.Val);
        // 尝试了通过调用 Update 以避免阻塞主线程，但会导致顺序混乱，未能解决
        // Available = false;
        // _ = Update();
    }

    public override async Task Update()
    {
        // 避免阻塞主线程   
        await Task.Run(() =>
        {
            lock (lockVal)
            {
                Unavilablize();
                val = ExprFunc(ExprA.Val, ExprB.Val);
                Available = true;
            }
        });
        if (parent != null)
        {
            await parent.Update();
        }
    }

    public override void Register(Expr parent)
    {
        this.parent = parent;
        parent.Update();
    }
}

class UnaryExpr : Expr
{
    int val = 0;
    readonly object lockVal = new();
    public override int Val
    {
        get
        {
            lock (lockVal)
            {
                return val;
            }
        }
    }

    public Expr ExprA;
    public Func<int, int> ExprFunc { get; private set; }
    public UnaryExpr(Expr A, Func<int, int> func)
    {
        ExprA = A;
        ExprFunc = func;
        SetParent(A, this);
        val = ExprFunc(ExprA.Val);
    }

    public override async Task Update()
    {
        // 避免阻塞主线程   
        await Task.Run(() =>
        {
            lock (lockVal)
            {
                Unavilablize();
                val = ExprFunc(ExprA.Val);
                Available = true;
            }
        });
        if (parent != null)
        {
            await parent.Update();
        }
    }

    public override void Register(Expr parent)
    {
        this.parent = parent;
        parent.Update();
    }
}

/// <summary>
/// 加法运算表达式结点
/// </summary>
public class AddExpr(Expr A, Expr B) : BinaryExpr(A, B, (a, b) => {
    Thread.Sleep(100);
    return a + b;
}) { }

/// <summary>
/// 乘法运算表达式结点
/// </summary>
public class MulExpr(Expr A, Expr B) : BinaryExpr(A, B, (a, b) => {
    Thread.Sleep(100);
    return a * b;
}) { }
