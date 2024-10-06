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

using System.Text.RegularExpressions;
using System.IO;
#endregion

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
	int numMembers,
	List<string> beamInfo,
	List<double> beamLength,
	List<double> bending,
	List<double> axialForceS,
	List<double> axialForceE,
	List<double> shearVzS,
	List<double> shearVzE,
	List<double> shearVyS,
	List<double> shearVyE,
	string materialsData,
	string filePath,
	bool save,
	int colNum,
	ref object memberLoc,
	ref object memberSection,
	ref object compositeNum,
	ref object widths,
	ref object heights,
	ref object lengths,
	ref object materials,
	ref object maxRatios,
	ref object bendingRatios,
	ref object compressionRatios,
	ref object shearRatios,
	ref object diagramCenterPt,
	ref object diagramSection,
	ref object subSection,
	ref object textLine1,
	ref object textLine2,
	ref object textLine3,
	ref object textLine4,
	ref object textLine5)
    {
        // get raw data from Karamba
        List<string> pos = new List<string>();
        List<Point3d> start_pts = new List<Point3d>();
        List<Point3d> end_pts = new List<Point3d>();
        for(int i = 0; i< beamInfo.Count; i++){
            string[] data = beamInfo[i].Split(';');
            pos.Add(data[1]);
        }
        for(int i = 0; i<pos.Count; i++){
          int index1 = pos[i].IndexOf(')');
          int index2 = pos[i].LastIndexOf(':');
          string sp = pos[i].Substring(5,index1-5+1);
          string ep = pos[i].Substring(index2+1);
          Point3d sPt = getPtFromString(sp);
          Point3d ePt = getPtFromString(ep);
          start_pts.Add(sPt);
          end_pts.Add(ePt);
        }

        // assign to beam object and store in list
        List<Beam> beams = new List<Beam>();
        for(int i = 0; i<start_pts.Count; i++){
            int check_com = checkCompression(axialForceS[i],axialForceE[i]);
            double check_axialForce = checkAxialForce(axialForceS[i],axialForceE[i]);
            double check_shear = checkShear(shearVzS[i],shearVzE[i],shearVyS[i],shearVyE[i]);
            Beam b = new Beam(i,
                              start_pts[i],
                              end_pts[i],
                              beamLength[i],
                              bending[i]*10197.16,
                              check_com,
                              check_axialForce*101.9716,
                              check_shear*101.9716);
            beams.Add(b);
        }

        beams = beams.OrderByDescending(b=>b.bending).ToList();

        // setup timber size list
        List<string> size_List = new List<string>{
            "2x6","2x8","2x10","2x12",
            "4x6","4x8","4x10","4x12",
            "6x6","6x8","6x10","6x12"
        };
        // setup random with seed
        Random r = new Random(42);


        // get prediction result from csv file
        List<string> img_paths = new List<string>();
        List<double> MOR_list = new List<double>();
        List<double> MOE_list = new List<double>();
        List<double> shearing_list = new List<double>();
        List<double> compression_list = new List<double>();
        
        var matData = File.ReadAllLines(materialsData);
        for(int i = 1; i<matData.Length; i++){
            var item = matData[i].Split(',');
            img_paths.Add(item[0]);
            MOR_list.Add(double.Parse(item[1]));
            MOE_list.Add(double.Parse(item[2]));
            shearing_list.Add(double.Parse(item[3]));
            compression_list.Add(double.Parse(item[4]));
        }

        // assign to timber object and store in list
        List<Timber> timbers = new List<Timber>();
        for(int i = 0; i<img_paths.Count; i++){
            int rd = r.Next(4);
            Timber t = new Timber(i,img_paths[i],MOR_list[i],MOE_list[i],shearing_list[i],compression_list[i],size_List[rd]);
            double tWidth = getActualWidth(size_List[rd]);
            double tHeight = getActualHeight(size_List[rd]);
            t.tWidth = tWidth;
            t.tHeight = tHeight;
            // section module = (b * h * h)/6
            double s_value = (tWidth*tHeight*tHeight)/6;
            double s_area = tWidth*tHeight;
            t.maxBending = t.MOR*s_value;
            t.maxCompression = t.compression * s_area;
            t.maxShear = (t.shear * s_area * 2)/3;
            timbers.Add(t);
        }

        timbers = timbers.OrderByDescending(t=>t.maxBending).ToList();
        List<int> compositeNumList = new List<int>();
        List<double> widthList = new List<double>();
        List<double> heightList = new List<double>();
        List<double> lengthList = new List<double>();
        List<int> materialIDList = new List<int>();
        List<double> maxRatioList = new List<double>();
        List<double> ratioOfBendingList = new List<double>();
        List<double> ratioOfCompressionList = new List<double>();
        List<double> ratioOfShearList = new List<double>();

        // match timber to beam
        for(int i = 0; i<beams.Count; i++){
            if(beams[i].compression == 0){
                // for compression member, check all structural requirement
                // calculate the using number of timber
                int b = Convert.ToInt32(Math.Ceiling(beams[i].bending / timbers[i].maxBending));
                int c = Convert.ToInt32(Math.Ceiling(beams[i].axialForce / timbers[i].maxCompression));
                int s = Convert.ToInt32(Math.Ceiling(beams[i].shear / timbers[i].maxShear));
                int n = Math.Max(b,Math.Max(c,s));

                // Euler's buckling load Pcr = (pi^2 * E * I)/(KL)^2
                double require_i_value = beams[i].axialForce*Math.Pow(beams[i].bLength*100,2)/(Math.Pow(Math.PI,2)*timbers[i].MOE);
                double current_i_value = (timbers[i].tHeight* Math.Pow(timbers[i].tWidth*n,3))/12;
                if(require_i_value>current_i_value){
                    double require_width = Math.Pow((12* require_i_value)/timbers[i].tHeight,1.0/3);
                    int require_timberNum = Convert.ToInt32(Math.Ceiling(require_width/timbers[i].tWidth));
                    n = require_timberNum;
                }
                double pcr = (Math.Pow(Math.PI,2)*timbers[i].MOE*((timbers[i].tHeight*Math.Pow(timbers[i].tWidth*n,3))/12))/(Math.Pow(beams[i].bLength*100,2));
                
                beams[i].compositeNum = n;
                beams[i].materialID = timbers[i].timberID;
                beams[i].width = timbers[i].tWidth*n;
                beams[i].height = timbers[i].tHeight;
                beams[i].ratioOfBending = Math.Round((beams[i].bending / (timbers[i].maxBending*n)),2);
                beams[i].ratioOfCompression = Math.Max(Math.Round((beams[i].axialForce / (timbers[i].maxCompression*n)),2),
                                                                    beams[i].axialForce/ pcr);
                beams[i].ratioOfShear = Math.Round((beams[i].shear / (timbers[i].maxShear*n)),2);
                beams[i].maxRatio = Math.Max(beams[i].ratioOfBending, Math.Max(beams[i].ratioOfCompression, beams[i].ratioOfShear));
            }else if(beams[i].compression == 1){
                // for tension member, check all structural requirement
                int b = Convert.ToInt32(Math.Ceiling(beams[i].bending / timbers[i].maxBending));
                int s = Convert.ToInt32(Math.Ceiling(beams[i].shear / timbers[i].maxShear));
                int n = Math.Max(b,s);
                
                beams[i].compositeNum = n;
                beams[i].materialID = timbers[i].timberID;
                beams[i].width = timbers[i].tWidth*n;
                beams[i].height = timbers[i].tHeight;
                beams[i].ratioOfBending = Math.Round((beams[i].bending / (timbers[i].maxBending*n)),2);
                beams[i].ratioOfCompression = 0;
                beams[i].ratioOfShear = Math.Round((beams[i].shear / (timbers[i].maxShear*n)),2);
                beams[i].maxRatio = Math.Max(beams[i].ratioOfBending, Math.Max(beams[i].ratioOfCompression, beams[i].ratioOfShear));
            }
            
        }

        beams = beams.OrderBy(b=>b.beamID).ToList();
        timbers = timbers.OrderBy(t=>t.timberID).ToList();

        foreach (var beam in beams)
        {
            compositeNumList.Add(beam.compositeNum);
            widthList.Add(beam.width);
            heightList.Add(beam.height);
            lengthList.Add(beam.bLength*100);
            materialIDList.Add(beam.materialID);
            maxRatioList.Add(beam.maxRatio);
            ratioOfBendingList.Add(beam.ratioOfBending);
            ratioOfCompressionList.Add(beam.ratioOfCompression);
            ratioOfShearList.Add(beam.ratioOfShear);
        }

        // visualization
        List<Line> lns = new List<Line>();
        List<Rectangle3d> sections = new List<Rectangle3d>();

        foreach (var beam in beams)
        {
            Line ln = new Line(beam.startPt,beam.endPt);
            double sZ = beam.startPt.Z;
            double eZ = beam.endPt.Z;
            lns.Add(ln);
            if(sZ == eZ){
                Rectangle3d rect = new Rectangle3d(Plane.WorldXY,
                                               new Interval(((beam.height/200)*-1),((beam.height/200)*1)),
                                               new Interval(((beam.width/200)*-1),((beam.width/200)*1)));
                sections.Add(rect);
            }else{
                Rectangle3d rect = new Rectangle3d(Plane.WorldXY,
                                               new Interval(((beam.width/200)*-1),((beam.width/200)*1)),
                                               new Interval(((beam.height/200)*-1),((beam.height/200)*1)));
                sections.Add(rect);
            }
        }

        List<Point3d> centerPts = new List<Point3d>();
        List<Rectangle3d> rects = new List<Rectangle3d>();
        List<Rectangle3d> subRects = new List<Rectangle3d>();
        List<string> strLine1 = new List<string>();
        List<string> strLine2 = new List<string>();
        List<string> strLine3 = new List<string>();
        List<string> strLine4 = new List<string>();
        List<string> strLine5 = new List<string>();

        // draw the sections
        for(int i = 0;i < beams.Count;i++)
        {
        // create a plane
        Point3d o = new Point3d((i % colNum) * 5, (i / colNum) * 5 * -1, 0);
        //dCenterPts.Add(o);
        Plane pl = new Plane(o, Vector3d.ZAxis);
        // create a rectangle
        Rectangle3d rect = new Rectangle3d(pl,beams[i].width/100,beams[i].height/100);
        Point3d rectCenter = rect.Center;
        rect.Transform(Transform.Translation(new Vector3d(o.X - rectCenter.X, o.Y - rectCenter.Y,0)));
        
            for(int j = 0; j<beams[i].compositeNum;j++){
            Rectangle3d subRect = new Rectangle3d(new Plane(new Point3d(o.X+(timbers[beams[i].materialID].tWidth*j)/100,o.Y,o.Z),Vector3d.ZAxis),
                                                  timbers[beams[i].materialID].tWidth/100,timbers[beams[i].materialID].tHeight/100);
            subRect.Transform(Transform.Translation(new Vector3d(o.X - rectCenter.X, o.Y - rectCenter.Y,0)));
            subRects.Add(subRect);
            }
            
        rects.Add(rect);
        Point3d textCenter = new Point3d(o.X +0.1, o.Y+0.1, 0);
        centerPts.Add(textCenter);
        
   
        string line1 = "Member Index: " + beams[i].beamID.ToString();
        string line2 = "Number of Composite: " + beams[i].compositeNum.ToString();
        string line3 = "From: ( " + Math.Round(beams[i].startPt.X,2).ToString() + ", " + Math.Round(beams[i].startPt.Y,2).ToString() + ", "+ Math.Round(beams[i].startPt.Z,2).ToString() + ")" +
                       "  To: ( " + Math.Round(beams[i].endPt.X,2).ToString() + ", " + Math.Round(beams[i].endPt.Y,2).ToString() + ", "+ Math.Round(beams[i].endPt.Z,2).ToString() + ")";
        string line4 = "Width: " + Math.Round(beams[i].width,2).ToString() +" cm" + "   Height: " + Math.Round(beams[i].height,2).ToString() + " cm";
        string line5 = "Material Index: " + beams[i].materialID;
        strLine1.Add(line1);
        strLine2.Add(line2);
        strLine3.Add(line3);
        strLine4.Add(line4);
        strLine5.Add(line5);
        
        }

        // save csv file
        if(save==true){
            SaveCSV(filePath,beams,timbers);
        }

        
        memberLoc = lns;
        memberSection = sections;
        compositeNum = compositeNumList;
        widths = widthList;
        heights = heightList;
        lengths = lengthList;
        materials = materialIDList;
        maxRatios = maxRatioList;
        bendingRatios = ratioOfBendingList;
        compressionRatios = ratioOfCompressionList;
        shearRatios = ratioOfShearList;
        diagramCenterPt = centerPts;
        diagramSection = rects;
        subSection = subRects;
        textLine1 = strLine1;
        textLine2 = strLine2;
        textLine3 = strLine3;
        textLine4 = strLine4;
        textLine5 = strLine5;
    }

    public Point3d getPtFromString(string a){
        // Remove the parentheses
        string cleanedString = a.Trim('(',')');
        // Split the string by commas
        string[] parts = cleanedString.Split(',');
        // Parse the x, y, z coordinates
        double x = double.Parse(parts[0].Trim());
        double y = double.Parse(parts[1].Trim());
        double z = double.Parse(parts[2].Trim());
        // Create a new Point3d object and add it to the list
        Point3d pt = new Point3d(x,y,z);
        return pt;
    }

    public int checkCompression(double a, double b){
        int check=-1;
        if (a<0 && b <0){
            check=0;
        }else if (a>=0 && b>=0){
            check=1;
        }else if (a*b<=0){
            if(Math.Abs(a)>Math.Abs(b)){
                if(a<0){
                    check=0;
                }else{
                    check=1;
                }
            }else{
                if(b<0){
                    check=0;
                }else{
                    check=1;
                }
            }
        }
        return check;
    }

    public double checkAxialForce(double a, double b){
        double force = 0;
        if(Math.Abs(a)>Math.Abs(b)){
            force = Math.Abs(a);
        }else{
            force = Math.Abs(b);
        }
        return force;
    }

    public double checkShear(double a, double b, double c, double d){
        double shear = 0;
        double _a = Math.Abs(a);
        double _b = Math.Abs(b);
        double _c = Math.Abs(c);
        double _d = Math.Abs(d);
        shear = Math.Max(_a, Math.Max(_b, Math.Max(_c,_d)));

        return shear;
    }

    public double getActualWidth(string a){
        double width = 0;
        if(a=="2x6" || a=="2x8" || a=="2x10" || a=="2x12"){
            width=3.8;
        }else if(a=="4x6" || a=="4x8" || a=="4x10" || a=="4x12"){
            width=8.9;
        }else if(a=="6x6" || a=="6x8" || a=="6x10" || a=="6x12"){
            width=14;
        }
        return width;
    }

    public double getActualHeight(string a){
        double height = 0;
        if(a=="2x6" || a=="4x6" || a=="6x6"){
            height=14;
        }else if(a=="2x8" || a=="4x8" || a=="6x8"){
            height=18.4;
        }else if(a=="2x10" || a=="4x10" || a=="6x10"){
            height=23.5;
        }else if(a=="2x12" || a=="4x12" || a=="6x12"){
            height=28.6;
        }
        return height;
    }
    public void SaveCSV(string savePath, List<Beam> beams, List<Timber> timbers){
        string[] outputBeams = new string[beams.Count+1];
        outputBeams[0] = "Member Index" + ","
                        +"startPt.X" + ","
                        +"startPt.Y" + ","
                        +"startPt.Z" + ","
                        +"endPt.X" + ","
                        +"endPt.Y" + ","
                        +"endPt.Z" + ","
                        +"Number of Composite" + ","
                        +"Material ID" + ","
                        +"Beam Width" + ","
                        +"Beam Height" + ","
                        +"Timber Image"
                        ;
            for (int i = 0; i < beams.Count; i++){
                outputBeams[i+1] = i.ToString() + ","
                + Math.Round(beams[i].startPt.X,2).ToString() + "," 
                + Math.Round(beams[i].startPt.Y,2).ToString() + ","
                + Math.Round(beams[i].startPt.Z,2).ToString() + ","
                + Math.Round(beams[i].endPt.X,2).ToString() + "," 
                + Math.Round(beams[i].endPt.Y,2).ToString() + ","
                + Math.Round(beams[i].endPt.Z,2).ToString() + ","
                + beams[i].compositeNum.ToString() + ","
                + beams[i].materialID.ToString() + ","
                + Math.Round(beams[i].width,2).ToString() + ","
                + Math.Round(beams[i].height,2).ToString() + ","
                + timbers[beams[i].materialID].img_path
                ;
            }
        File.WriteAllLines(savePath, outputBeams);
    }
}


public class Beam{
    public int beamID;
    public Point3d startPt;
    public Point3d endPt;
    public double bLength;
    public double bending;
    public int compression; //0 for compression, 1 for tension
    public double axialForce;
    public double shear;
    public int materialID;
    public int compositeNum;
    public double width;
    public double height;
    public double ratioOfBending;
    public double ratioOfCompression;
    public double ratioOfShear;
    public double maxRatio;

    public Beam(int beamID,
                Point3d startPt, 
                Point3d endPt,
                double bLength,
                double bending,
                int compression,
                double axialForce,
                double shear){
        this.beamID = beamID;
        this.startPt = startPt;
        this.endPt = endPt;
        this.bLength = bLength;
        this.bending = bending;
        this.compression = compression;
        this.axialForce = axialForce;
        this.shear = shear;
    }
}

public class Timber{
    public int timberID;
    public string img_path;
    public double MOR;
    public double MOE;
    public double shear;
    public double compression;
    public string nominalSize;
    public double tWidth;
    public double tHeight;
    public double maxBending;
    public double maxCompression;
    public double maxShear;

    public Timber(int timberID,
                  string img_path,
                  double MOR,
                  double MOE,
                  double shear,
                  double compression,
                  string nominalSize){
        this.timberID = timberID;
        this.img_path = img_path;
        this.MOR = MOR;
        this.MOE = MOE;
        this.shear = shear;
        this.compression = compression;
        this.nominalSize = nominalSize;
    }
}
