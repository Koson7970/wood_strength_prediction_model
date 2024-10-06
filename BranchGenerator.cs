// Grasshopper Script Instance
#region Usings
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;

using Rhino;
using Rhino.Geometry;

using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
#endregion
using Grasshopper.Kernel.Geometry;
using System.IO;
public class Script_Instance : GH_ScriptInstance
{
    #region Notes
    /* 
      Members:
        RhinoDoc RhinoDocument
        GH_Document GrasshopperDocument
        IGH_Component Component
        int Iteration

      Methods (Virtual & overridable):
        Print(string text)
        Print(string format, params object[] args)
        Reflect(object obj)
        Reflect(object obj, string method_name)
    */
    #endregion

    private void RunScript(
	List<Line> lns,
	double edgeRange,
	List<Line> borders,
	double moduleSize,
	double maxDist,
	double density,
	double ceilingHeight,
	int numOfTreetopLayers,
	List<Point3d> corners,
	double siteArea,
	bool reset,
	ref object basePts,
	ref object upperPts,
	ref object a)
    {
        if(reset){
            moduleSize_ = moduleSize;
            possibleColumnsNum = 0;
            testColumnsNum = 0;
            possibleBranchesNum = 0;
            testBranchesNum = 0;

            reset = false;
            //generate gird
            GenerateGrids(edgeRange);
            CheckAllowabilityGrids(lns, borders, maxDist);

            
            for (int i = 0; i < grids.GetLength(0); i++)
            {
                for (int j = 0; j < grids.GetLength(1); j++)
                {
                    if (grids[i, j].allowability == 1)
                    {
                        possibleColumnsNum++;
                    }
                }
            }

            // create first level start points
            testColumnsNum = (int) (density * possibleColumnsNum);
            GenerateCols(testColumnsNum, grids);

            // create tree top level
            GenerateTGrids(edgeRange, numOfTreetopLayers, ceilingHeight);
            for (int i = 0; i < tGrids.GetLength(0); i++)
            {
                for (int j = 0; j < tGrids.GetLength(1); j++)
                {
                    for (int k = 0; k < tGrids.GetLength(2); k++){
                        if (tGrids[i, j, k].allowability == 1)
                        {
                            possibleBranchesNum++;
                        }                        
                    }

                }
            }
            // create branches points
            testBranchesNum = (int) (density * possibleBranchesNum);
            GenerateBranches(testBranchesNum,tGrids,ceilingHeight);
        }
        basePts = startPtsList;
        upperPts = branchesPtsList;
        
        
    }

    private static Random rnd = new Random();
    public static Grid[,] grids;
    public static Grid[,,] tGrids;
    public static double moduleSize_;

    public static int possibleColumnsNum;
    public static int testColumnsNum;

    public static List<Point3d> startPtsList = new List<Point3d>();
    public static List<int> testOccup = new List<int>();

    public static int possibleBranchesNum;
    public static int testBranchesNum;

    public static List<Point3d> branchesPtsList = new List<Point3d>();
    public static List<int> testBranchesOccup = new List<int>();


    public void GenerateGrids(double edgeRange)
    {
        grids = new Grid[(int) (edgeRange / moduleSize_), (int) (edgeRange / moduleSize_)];
        for (int i = 0; i < (edgeRange / moduleSize_); i++)
        {
            for (int j = 0; j < (edgeRange / moduleSize_); j++)
            {
                grids[i, j] = new Grid(0);
                grids[i, j].centerPt = new Point3d((i + 0.5) * moduleSize_, (j + 0.5) * moduleSize_, 0);
                grids[i, j].onEdge = 0;
            }
        }
    }

    public void GenerateTGrids(double edgeRange, int layers, double height)
    {
        tGrids = new Grid[(int) (edgeRange / moduleSize_), (int) (edgeRange / moduleSize_), layers];
        for (int i = 0; i < (edgeRange / moduleSize_); i++)
        {
            for (int j = 0; j < (edgeRange / moduleSize_); j++)
            {
                for(int k = 1; k < layers+1; k++){
                    tGrids[i, j, k-1] = new Grid(0);
                    tGrids[i, j, k-1].centerPt = new Point3d((i + 0.5) * moduleSize_, (j + 0.5) * moduleSize_, k * moduleSize_ + height);
                    tGrids[i, j, k-1].onEdge = 0;
                    tGrids[i, j, k-1].allowability = 1;
                }

            }
        }
    }

    public void CheckAllowabilityGrids(List<Line> lns, List<Line> borders, double maxDist)
    {
        for (int i = 0; i < grids.GetLength(0); i++)
        {
            for (int j = 0; j < grids.GetLength(1); j++)
            {
                Point3d pt = grids[i, j].centerPt;
                //distance to the closest line
                List<double> dists = new List<double>();
                for (int k = 0; k < lns.Count; k++)
                {
                    dists.Add(pt.DistanceTo(lns[k].ClosestPoint(pt, true)));
                }
                dists.Sort();
                //distance to the borders
                List<double> dists2 = new List<double>();
                for (int k = 0; k < borders.Count; k++)
                {
                    dists2.Add(pt.DistanceTo(borders[k].ClosestPoint(pt, true)));
                }
                dists2.Sort();
                if (dists[0] < maxDist && dists2[0] > 0.5 * maxDist)
                {
                    grids[i, j].allowability = 1;
                }
                else
                {
                    grids[i, j].allowability = 0;
                }
            }
        }
    }

    public void GenerateCols(int testColumnsNum, Grid[,] grids)
    {
        startPtsList.Clear();

        int allowGridsNum = 0;
        for (int i = 0; i < grids.GetLength(0); i++)
        {
            for (int j = 0; j < grids.GetLength(1); j++)
            {
                if (grids[i, j].allowability == 1)
                {
                    allowGridsNum++;
                }
            }
        }

        List<int> occupiedGrids = new List<int>();
        for (int i = 0; i < testColumnsNum; i++)
        {
            occupiedGrids.Add(1);
        }
        for (int i = 0; i < allowGridsNum - testColumnsNum; i++)
        {
            occupiedGrids.Add(0);
        }
        var shuffle_occupiedGrids = occupiedGrids.OrderBy(o => rnd.Next()).ToList();
            testOccup = shuffle_occupiedGrids;

        var a = 0;

        //asssign the occupied grids
        for (int i = 0; i < grids.GetLength(0); i++)
        {
            for (int j = 0; j < grids.GetLength(1); j++)
            {
                if (grids[i, j].allowability == 1)
                {
                    grids[i, j].occupied = shuffle_occupiedGrids[a];
                    a++;
                }       
                else
                {
                    grids[i, j].occupied = 0;
                }
            }
        }

        //create a list of points
        for (int i = 0; i < grids.GetLength(0); i++)
        {
            for (int j = 0; j < grids.GetLength(1); j++)
            {
                if (grids[i, j].occupied == 1)
                {
                    var x = rnd.NextDouble();
                    var y = rnd.NextDouble();
                    if (x < 0.2)
                    {
                        x = 0;
                        grids[i, j].onEdge = 1;
                    }
                    else if (x > 0.8)
                    {
                        x = 1;
                        grids[i, j].onEdge = 1;
                    }
                    if (y < 0.2)
                    {
                        y = 0;
                        grids[i, j].onEdge = 1;
                    }
                    else if (y > 0.8)
                    {
                        y = 1;
                        grids[i, j].onEdge = 1;
                    }

                    Point3d pt = new Point3d((i + x) * moduleSize_, (j + y) * moduleSize_, 0);
                    grids[i, j].innerPt = pt;
                    startPtsList.Add(pt);
                    
                }
            }
        }
    }

    
    public void GenerateBranches(int testBranchesNum, Grid[,,] tGrids, double height)
    {
        branchesPtsList.Clear();

        int allowGridsNum = 0;
        for (int i = 0; i < tGrids.GetLength(0); i++)
        {
            for (int j = 0; j < tGrids.GetLength(1); j++)
            {
                for (int k = 0; k < tGrids.GetLength(2); k++){
                    if (tGrids[i, j, k].allowability == 1)
                    {
                        allowGridsNum++;
                    } 
                }
            }
        }
        

        List<int> occupiedGrids = new List<int>();
        for (int i = 0; i < testBranchesNum; i++)
        {
            occupiedGrids.Add(1);
        }
        for (int i = 0; i < allowGridsNum - testBranchesNum; i++)
        {
            occupiedGrids.Add(0);
        }
        var shuffle_occupiedGrids = occupiedGrids.OrderBy(o => rnd.Next()).ToList();
            testBranchesOccup = shuffle_occupiedGrids;

        var a = 0;

        //asssign the occupied grids
        
        for (int i = 0; i < tGrids.GetLength(0); i++)
        {
            for (int j = 0; j < tGrids.GetLength(1); j++)
            {
                for (int k = 0; k < tGrids.GetLength(2); k++){
                    if (tGrids[i, j, k].allowability == 1)
                    {
                        tGrids[i, j, k].occupied = shuffle_occupiedGrids[a];
                        a++;
                    }       
                    else
                    {
                        tGrids[i, j, k].occupied = 0;
                    }
                }

            }
        }

        //create a list of points
        for (int i = 0; i < tGrids.GetLength(0); i++)
        {
            for (int j = 0; j < tGrids.GetLength(1); j++)
            {
                for (int k = 0; k < tGrids.GetLength(2); k++){
                    if (tGrids[i, j, k].occupied == 1)
                    {
                        var x = rnd.NextDouble();
                        var y = rnd.NextDouble();
                        var z = rnd.NextDouble();
                        if (x < 0.2)
                        {
                            x = 0;
                            tGrids[i, j, k].onEdge = 1;
                        }
                        else if (x > 0.8)
                        {
                            x = 1;
                            tGrids[i, j, k].onEdge = 1;
                        }
                        if (y < 0.2)
                        {
                            y = 0;
                            tGrids[i, j, k].onEdge = 1;
                        }
                        else if (y > 0.8)
                        {
                            y = 1;
                            tGrids[i, j, k].onEdge = 1;
                        }

                        Point3d pt = new Point3d((i + x) * moduleSize_, (j + y) * moduleSize_, (k + z)* moduleSize_+height);
                        tGrids[i, j, k].innerPt = pt;
                        branchesPtsList.Add(pt);
                    
                    }
                }

            }
        }
        
    }
    
}

public class Grid
{
    public int occupied; //0 = empty, 1 = occupied
    public Point3d innerPt;
    public Point3d centerPt;
    public int allowability; //0 = not allowed, 1 = allowed
    public int onEdge; //0 = not on edge, 1 = on edge
    public Grid(int occupied)
    {
      this.occupied = occupied;
    }
}
