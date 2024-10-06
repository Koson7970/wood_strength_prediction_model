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
	Curve boundary,
	int numOfSpace,
	bool generate,
	ref object Cells,
	ref object edgeLength,
	ref object corners,
	ref object EccentricityRate)
    {
        BoundingBox bx = boundary.GetBoundingBox(true);
        Line[] edges = bx.GetEdges();
        Point3d[] bxCorners = bx.GetCorners();
        List<Point3d> cornerPts = new List<Point3d>();
        foreach(Point3d p in bxCorners)
        {
            cornerPts.Add(p);
        }

        Node2List nodes = new Node2List();
        Node2List outline = new Node2List();

        //vornoi algorithm
        if (generate || redo)
        {
            cellPts.Clear();
            lengthEdges.Clear();
            polys.Clear();
            generate = false;
            redo = false;

            for(int i = 0; i < edges.Length; i++)
            {
                if (edges[i] != null)
                {
                    lengthEdges.Add(edges[i].Length);
                }
            }
            lengthEdges.Sort();
            lengthEdges.Reverse();

            GetVoronoi(numOfSpace, boundary, nodes, bxCorners, outline);

        }
        geoCenter = new Point3d(0, 0, 0);
        foreach (Point3d p in cornerPts)
        {
            geoCenter += p;
        }
        geoCenter /= cornerPts.Count;
        averageCenter = new Point3d(0, 0, 0);
        foreach (Point3d p in cellPts)
        {
            averageCenter += p;
        }
        averageCenter /= cellPts.Count;
        eccentricity = geoCenter.DistanceTo(averageCenter) / lengthEdges[0];
        if(eccentricity > 0.05)
        {
            redo = true;
        }

        Cells = polys;
        edgeLength = lengthEdges[0];
        corners = cornerPts;
        EccentricityRate = eccentricity;
    }

    public static List<Point3d> cellPts = new List<Point3d>();
    public static Random rdn = new Random();

    public static List<double> lengthEdges = new List<double>();
    public static List<Polyline> polys = new List<Polyline>();


    public static Point3d geoCenter;
    public static Point3d averageCenter;
    public static double eccentricity;
    public static bool redo = false;

    public static void GetVoronoi(int count, Curve boundary, Node2List nodes, Point3d[] bxCorners, Node2List outline)
    {
        cellPts.Clear();
        polys.Clear();
        //create random points
        for (int i = 0; i < count; i++)
        {
            double x = rdn.NextDouble();
            double y = rdn.NextDouble();
            Point3d pt = new Point3d(x * lengthEdges[0], y * lengthEdges[0], 0);
            //check if point is inside the boundary
            if (boundary.Contains(pt, Rhino.Geometry.Plane.WorldXY, 0.001) == PointContainment.Inside)
            {
                cellPts.Add(pt);
            }
            else
            {
                i--;
            }
            }

        //referenced from >>https://www.grasshopper3d.com/forum/topics/feature-request-access-to-grasshopper-scripts-in-python-c?commentId=2985220%3AComment%3A678528
        //Script from Anders Holden Deleuran
        //Tranformed in C# by Laurent Delrieu
        foreach (Point3d p in cellPts)
        {
            Node2 n = new Node2(p.X, p.Y);
            nodes.Append(n);
        }

        foreach (Point3d p in bxCorners)
        {
            Node2 n = new Node2(p.X, p.Y);
            outline.Append(n);
        }
        //Calculate the delaunay triangulation
        var delaunay = Grasshopper.Kernel.Geometry.Delaunay.Solver.Solve_Connectivity(nodes, 0.1, false);

        //Calculate the voronoi diagram
        var voronoi = Grasshopper.Kernel.Geometry.Voronoi.Solver.Solve_Connectivity(nodes, delaunay, outline);

        foreach (var c in voronoi)
        {
            Polyline pl = c.ToPolyline();
            Brep bp = Brep.CreatePlanarBreps(pl.ToNurbsCurve(), 0.01)[0];
            if (bp.GetArea() > lengthEdges[0] * lengthEdges[1] / count * 0.5 && bp.GetArea() < lengthEdges[0] * lengthEdges[1] / count * 1.5)
            {
                polys.Add(pl);
            }
            else
            {
                redo = true;
                break;
            }
        }
    }
}
