using Godot;
using System;
using System.Collections.Generic;
using System.Linq.Dynamic.Core;
using System.Linq.Dynamic.Core.Parser;
using System.Linq.Expressions;
using System.Reflection;

public class Calculator : Node2D
{
    // Declare member variables here. Examples:
    // private int a = 2;
    // private string b = "text";
    private Button graphButton;
    private Panel panel;
    private Label coordLabel;
    private LineEdit stepLine, equationLine, domainLine, rangeLine, slopeLineLine;

    private float leftBound = -1;
    private float rightBound = 1;
    private float upperBound = 1;
    private float lowerBound = -1;
    private float step = 0.1f;
    private int slopeLineRowCount = 10;
    private float[][] yPValues = new float[0][];
    private float yStart, yEnd, xStart, xEnd, length;
    private Vector2 initialValue;
    private bool drawIVP = false;
    private bool hasGraphed = false;
    private Func<float, float, float> diffEq = null;

    /// <summary>
    /// Called when the node enters the scene tree for the first time.
    /// </summary>
    public override void _Ready()
    {
        panel = GetNode<Panel>("Panel");
        graphButton = GetNode<Button>("Panel/Button");
        graphButton.Connect("pressed", this, "Graph");
        stepLine = GetNode<LineEdit>("Panel/Step");
        equationLine = GetNode<LineEdit>("Panel/Equation");
        domainLine = GetNode<LineEdit>("Panel/Domain");
        rangeLine = GetNode<LineEdit>("Panel/Range");
        slopeLineLine = GetNode<LineEdit>("Panel/SlopeLineNumber");

        coordLabel = GetNode<Label>("CoordinateLabel");

        // assume a square drawing area
        initializeDrawArea();

        length = xEnd;
        MathMethods.Initialize();
    }

    private void initializeDrawArea()
    {
        yStart = panel.RectSize.y;
        yEnd = yStart + panel.RectSize.x;
        xEnd = panel.RectSize.x;
        xStart = 0;
    }

    private void Graph()
    {
        GD.Print($"Graphing y'={equationLine.Text} on x=[${domainLine.Text}] and y=[${rangeLine.Text}] with {slopeLineLine.Text} slope lines per row");
        drawIVP = false;
        try
        {
            slopeLineRowCount = int.Parse(slopeLineLine.Text);
        } catch (Exception err)
        {
            GD.PrintErr("Could not parse row count: " + err.Message);
            hasGraphed = false;
            return;
        }

        // parse the range
        try
        {
            string[] ranges = rangeLine.Text.Split(',');
            lowerBound = float.Parse(ranges[0]);
            upperBound = float.Parse(ranges[1]);
        } catch (Exception err)
        {
            GD.PrintErr("Could not parse range: " + err.Message);
            hasGraphed = false;
            return;
        }

        try
        {
            // parse the domain
            string[] domains = domainLine.Text.Split(',');
            leftBound = float.Parse(domains[0]);
            rightBound = float.Parse(domains[1]);
        }
        catch (Exception err)
        {
            GD.PrintErr("Could not parse domain: " + err.Message);
            hasGraphed = false;
            return;
        }

        // create a graph
        // parse the equation input into a string that looks like a usuable lambda expression
        string eq = FilterEquation(equationLine.Text);
        GD.Print(eq);
        LambdaExpression e = null;

        // convert the string into an actual lambda expression
        try
        {
            ParameterExpression[] config = new ParameterExpression[] { System.Linq.Expressions.Expression.Parameter(typeof(float), "x"), 
            System.Linq.Expressions.Expression.Parameter(typeof(float), "y") };
            e = DynamicExpressionParser.ParseLambda(config
                , typeof(float), eq, new MathMethods());

            // if everything works, compile it
            diffEq = e.Compile() as Func<float, float, float>;
        }
        catch(Exception err)
        {
            GD.PrintErr($"Could not parse the equation into lambda expression: {err.Message}");
            hasGraphed = false;
            return;
        }

        try
        {
            yPValues = new float[slopeLineRowCount][];
            float xStep = getStep(leftBound, rightBound, slopeLineRowCount);
            float yStep = getStep(lowerBound, upperBound, slopeLineRowCount);
            // calculate all the slope values for the given position
            for (int y = 0; y < yPValues.Length; y++)
            {
                yPValues[y] = new float[slopeLineRowCount];
                for (int x = 0; x < yPValues[y].Length; x++)
                {
                    float xVal = leftBound + x * xStep;
                    float yVal = lowerBound + y * yStep;
                    var result = diffEq.DynamicInvoke(xVal, yVal);
                    try
                    {
                        yPValues[y][x] = (float)result;
                    }
                    catch (Exception err)
                    {
                        GD.PrintErr($"{err.Message}");
                        GD.Print(result);
                        yPValues[y][x] = float.NaN;
                    }
                }
            }
        }
        catch (Exception err)
        {
            GD.PrintErr("Could not compute slopes: " + err.Message);
            hasGraphed = false;
            return;
        }

        hasGraphed = true;

        // graph the function
        Update();
    }

    /// <summary>
    /// Converts coordinate space to screen space
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    private Vector2 CoordToScreen(Vector2 position)
    {
        return new Vector2(length * (position.x - leftBound) / (rightBound - leftBound),
            (1 - (position.y - lowerBound) / (upperBound - lowerBound)) * length + yStart);
    }

    /// <summary>
    /// Converts screen space to coordinate space.
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    private Vector2 ScreenToCoord(Vector2 position)
    {
        return new Vector2(position.x / length * (rightBound - leftBound) + leftBound,
            (1 - (position.y - yStart) / length) * (upperBound - lowerBound) + lowerBound);
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mouseMotion)
        {
            Vector2 coord = ScreenToCoord(mouseMotion.Position);
            coordLabel.Text = $"({coord.x}, {coord.y})";
            if (isWithinGraphingArea(mouseMotion.Position) && hasGraphed)
            {
                initialValue = ScreenToCoord(mouseMotion.Position);
                drawIVP = true;
                Update();
            }
        }
    }

    private bool isWithinGraphingArea(Vector2 position)
    {
        return position.x >= 0 && position.x <= length && position.y >= yStart && position.y <= yStart + length;
    }
    
    private string FilterEquation(string eq)
    {
        for (int i = 0; i < MathMethods.FunctionNames.Length; i++)
        {
            List<int> indices = eq.AllIndexesOf(MathMethods.FunctionNames[i]);
            for (int j = indices.Count - 1; j >= 0; j--)
            {
                eq = eq.Insert(indices[j], "@0.");
            }
        }
        return eq;
    }

    private float getStep(float lower, float upper, int count)
    {
        return (upper - lower) / (count - 1);
    }

    private void DrawEulerApproximation(Vector2 initialValue)
    {
        // parse step size
        step = float.Parse(stepLine.Text);
        // GD.Print($"Running Euler's method approximation for the solution curve of y'= {equationLine.Text} @ ({initialValue.x}, {initialValue.y}) with step size ${step}");

        // forward
        Color solutionColor = new Color(1, 0, 0);
        float ix = initialValue.x;
        float iy = initialValue.y;
        float prevY = iy;
        int forwardSteps = Mathf.CeilToInt((rightBound - ix) / step);
        for (int i = 1; i <= forwardSteps; i++)
        {
            float x = ix + i * step;
            float dy = (float)diffEq.DynamicInvoke(x, prevY);
            if (float.IsInfinity(dy))
            {
                continue;
            }
            float y = prevY + dy * step;
            Vector2 p1 = CoordToScreen(new Vector2(x - step, prevY));
            Vector2 p2 = CoordToScreen(new Vector2(x, y));

            DrawLine(p1, p2, solutionColor, 2, true);

            prevY = y;
        }

        prevY = iy;
        // backwards
        int backwardSteps = Mathf.CeilToInt((ix - leftBound) / step);
        for (int i = 1; i <= backwardSteps; i++)
        {
            float x = ix - i * step;
            float y = prevY - (float)diffEq.DynamicInvoke(x, prevY) * step;
            Vector2 p1 = CoordToScreen(new Vector2(x + step, prevY));
            Vector2 p2 = CoordToScreen(new Vector2(x, y));

            DrawLine(p1, p2, solutionColor, 2, true);

            prevY = y;
        }
    }

    public override void _Draw()
    {
        base._Draw();

        // draw the IVP
        if (drawIVP)
        {
            DrawEulerApproximation(initialValue);
        }

        // draw the axis
        // x-axis
        Vector2 zero = CoordToScreen(Vector2.Zero);
        if (!(lowerBound > 0 || upperBound < 0))
        {
            DrawLine(new Vector2(0, zero.y), new Vector2(length, zero.y), new Color(1, 1, 1), 4);
            // float ratio = 1 - Math.Abs(lowerBound / (upperBound - lowerBound));
            // DrawLine(new Vector2(xStart, yStart + xEnd * ratio), new Vector2(xEnd, yStart + xEnd * ratio), new Color(1, 1, 1), 4);
        }
        // y-axis
        if (!(rightBound < 0 || leftBound > 0))
        {
            DrawLine(new Vector2(zero.x, yStart), new Vector2(zero.x, yStart + length), new Color(1, 1, 1), 4);
            // float ratio = 1 - Math.Abs(rightBound / (rightBound - leftBound));
            // DrawLine(new Vector2(xStart + ratio * length, yStart), new Vector2(xStart + ratio * length, yStart + length), new Color(1, 1, 1), 4);
        }

        // DrawLine(GetLinePosition(0.2f, -1), GetLinePosition(0.2f, 1), new Color(255, 255, 255), 2);

        // draw the function
        if (yPValues.Length > 1)
        {
            float xStep = getStep(leftBound, rightBound, slopeLineRowCount);
            float yStep = getStep(lowerBound, upperBound, slopeLineRowCount);
            float lineLength = length / slopeLineRowCount / 2f * 0.9f;
            float screenProportion =  (rightBound - leftBound) / (upperBound - lowerBound);
            for (int y = 0; y < yPValues.Length; y++)
            {
                for (int x = 0; x < yPValues[y].Length; x++)
                {
                    float xVal = leftBound + x * xStep;
                    float yVal = lowerBound + y * yStep;
                    Vector2 point = CoordToScreen(new Vector2(xVal, yVal));
                    float angle = -Mathf.Atan(yPValues[y][x] * screenProportion);
                    Vector2 diff = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * lineLength;
                    DrawLine(point - diff, point + diff, new Color(1, 1, 1), 1);
                }
            }
        }
    }

    private Vector2 GetLinePosition(float x, float y)
    {
        return new Vector2(getUVPosition(x, leftBound, rightBound) * length + xStart, getUVPosition(y, lowerBound, upperBound) * length + yStart);
    }

    private float getUVPosition(float value, float lower, float upper)
    {
        if (value > upper || value < lower)
            return 0;
        
        return (value - lower) / (upper - lower);
    }
}

public class MathMethods
{

    #region Constants
    public float pi = Mathf.Pi;
    public float e = Mathf.E;
    #endregion
    public static string[] FunctionNames;

    /// <summary>
    /// Stores all the names of the functions into the array of function names.
    /// </summary>
    public static void Initialize()
    {
        MethodInfo[] inf = typeof(MathMethods).GetMethods();
        FieldInfo[] fInf = typeof(MathMethods).GetFields();
        FunctionNames = new string[inf.Length + fInf.Length - 6];
        int j = 0;
        for (int i = 0; i < inf.Length; i++)
        {
            if (inf[i].Name == "Equals" || inf[i].Name == "GetHashCode" || inf[i].Name == "GetType" || inf[i].Name == "ToString" || inf[i].Name == "Initialize")
            {
                continue;
            }

            FunctionNames[j++] = inf[i].Name;
        }
        for (int i = 0; i < fInf.Length; i++)
        {
            if (fInf[i].Name == "FunctionNames")
                continue;
            FunctionNames[j++] = fInf[i].Name;
        }

        GD.Print("List of available functions (some might be buggy!)");
        for (int i = 0; i < FunctionNames.Length; i++)
        {
            GD.Print(FunctionNames[i]);
        }

        GD.Print("Have fun!");
    }

    #region Power Functions
    public float exp(float x)
    {
        return (float)Math.Exp(x);
    }
    public float sqrt(float x)
    {
        return (float)Math.Sqrt(x);
    }

    public float pow(float x, float y)
    {
        return (float)Math.Pow(x, y);
    }

    public float log(float x, float y)
    {
        return (float)Math.Log(x, y);
    }

    public float log(float x)
    {
        return (float)Math.Log10(x);
    }

    public float ln(float x)
    {
        return (float)Math.Log(x);
    }
    #endregion

    #region Trig Functions
    public float cos(float x)
    {
        return (float)Math.Cos(x);
    }

    public float sin(float x)
    {
        return (float)Math.Sin(x);
    }

    public float tan(float x)
    {
        return (float)Math.Tan(x);
    }

    public float atan(float x)
    {
        return (float)Math.Atan(x);
    }
    public float acos(float x)
    {
        return (float)Math.Acos(x);
    }
    public float asin(float x)
    {
        return (float)Math.Asin(x);
    }
    #endregion

    #region Hyperbolic Functions
    public float tanh(float x)
    {
        return (float)Math.Tanh(x);
    }

    public float cosh(float x)
    {
        return (float)Math.Cosh(x);
    }

    public float sinh(float x)
    {
        return (float)Math.Sinh(x);
    }
    #endregion
    
    #region Special Functions
    public float abs(float x)
    {
        return (float)Math.Abs(x);
    }

    // /// <summary>
    // /// Differentiates the function at the given position.
    // /// </summary>
    // /// <param name="eq"></param>
    // /// <param name="at"></param>
    // /// <param name="step"></param>
    // /// <returns></returns>
    // public float diff(Func<float, float> eq, float at, float step = 0.0000001f)
    // {
    //     return (eq.Invoke(at + step) - eq.Invoke(at)) / step;
    // }
    
    // /// <summary>
    // /// Calculates the integral of the function for the given bounds.
    // /// </summary>
    // /// <param name="eq"></param>
    // /// <param name="left"></param>
    // /// <param name="right"></param>
    // /// <param name="rectNum"></param>
    // /// <returns></returns>
    // public float integrate(Func<float, float> eq, float left, float right, int rectNum = 100)
    // {
    //     float integral = 0.0f;
    //     float dx = (right - left) / rectNum;
    //     for (int i = 0; i < rectNum; i++)
    //     {
    //         integral += dx * eq.Invoke(i * dx + left);
    //     }
    //     return integral;
    // }
    #endregion
}

public static class StringExtensionClass
{
    public static List<int> AllIndexesOf(this string str, string value)
    {
        if (String.IsNullOrEmpty(value))
            throw new ArgumentException("the string to find may not be empty", "value");
        List<int> indexes = new List<int>();
        for (int index = 0;; index += value.Length) {
            index = str.IndexOf(value, index);
            if (index == -1)
                return indexes;
            indexes.Add(index);
        }
    }
}
