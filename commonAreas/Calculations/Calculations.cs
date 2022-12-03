using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Visual;
using Autodesk.Revit.UI;
using RvView = Autodesk.Revit.DB.View;
using RvParam = Autodesk.Revit.DB.Parameter;
using Autodesk.Revit.DB.IFC;
using RvApp = Autodesk.Revit.ApplicationServices.Application;
using System.Reflection;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;

namespace commonAreas
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]

    public class Calculations: IExternalCommand
    {
        public bool showWallFinishandBaseboard = false;
        bool analyzeLevels = true;
        public string levelInput = string.Empty;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) {
            UIDocument uidoc = commandData.Application.ActiveUIDocument; //e) First attribute (commandData) is used to acces the Revit user interface
            Document doc = uidoc.Document; // accesing data to the current project
            RvView view = doc.ActiveView;
            StringBuilder stb = new StringBuilder();

            utilities.tailorBirdUtilities tailorBird = new utilities.tailorBirdUtilities();
            List<Element> levelsTot = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Levels).WhereElementIsNotElementType().ToList();
            List<string> levelNames = new List<string>();
            if (levelsTot.Any()) { 
                foreach (Element level in levelsTot)
                {
                    string nameLvl = level.Name;
                    levelNames.Add(nameLvl);
                }
            }

            commonAreas.calculationInterface calUI = new commonAreas.calculationInterface(levelNames);
            calUI.ShowDialog();
            if (calUI.DialogResult == false) {
                return Result.Succeeded;
            }

            if (calUI.ToggleMiguel.IsChecked == true) { showWallFinishandBaseboard = true; }



            SpatialElementBoundaryOptions sebOptions = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };

            //extras
            bool throwWarning = false;
            List<Tuple<string,string>> missingBaseboard= new List<Tuple<string, string>>();
            List<Tuple<string, string>> missingWallFinish = new List<Tuple<string, string>>();
            List<Tuple<string, string>> missingCaseworks = new List<Tuple<string, string>>();
            List<Tuple<string, string>> failedToTessellate = new List<Tuple<string, string>>();

            Transaction tr = new Transaction(doc, "Calculations - Common areas");
            tr.Start();
            #region 1) Common Areas

            //a) baseboard parameters
            double skirtingboardWidth = 0.1;
            double skirtingboardHeight = 0.4;
            RailingType railType = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StairsRailing).OfClass(typeof(RailingType)).Cast<RailingType>().Where(x => x.Name == "Baseboard 2").FirstOrDefault();
            Element wallType = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls).OfClass(typeof(WallType)).Where(x => x.Name.Contains("baseboards")).FirstOrDefault();
            
            //b) wall areas
            #region b.1) Material
            List<Element> materials = new FilteredElementCollector(doc).OfClass(typeof(Material)).ToElements().ToList();
            Material materialWallFinish = null;

            if (materials.Any(x => x.Name == "Tailorbird wallFinish Material"))
            {
                Material theMaterial = materials.First(x => x.Name == "Tailorbird wallFinish Material") as Material;
                if (theMaterial != null)
                {
                    materialWallFinish = theMaterial;
                }
            }
            else
            {
                //Look up the default wall material
                var defaultWall = materials.FirstOrDefault(x => x.Name == "Default Wall");
                if (defaultWall != null)
                {
                    Material theDefaultMaterial = defaultWall as Material;
                    if (theDefaultMaterial != null)
                    {

                        //duplicate its appearance asset
                        Element appearanceAssetEl = doc.GetElement(theDefaultMaterial.AppearanceAssetId);
                        AppearanceAssetElement appearanceAsset = appearanceAssetEl as AppearanceAssetElement;
                        if (appearanceAsset != null)
                        {
                            AppearanceAssetElement appearanceAssetDup = appearanceAsset.Duplicate("WallFinishDirectShape");

                            //create new material
                            Element newMaterialEl = doc.GetElement(Material.Create(doc, "Tailorbird wallFinish Material"));
                            Material newMaterial = newMaterialEl as Material;
                            if (newMaterial != null)
                            {

                                //set material color
                                newMaterial.Color = new Color(255, 0, 0);

                                //set the duplicated asset
                                newMaterial.AppearanceAssetId = appearanceAssetDup.Id;

                                //set color value to the new appearance asset
                                Element myAppearanceAssetEl = doc.GetElement(newMaterial.AppearanceAssetId);
                                AppearanceAssetElement myAppearanceAsset = myAppearanceAssetEl as AppearanceAssetElement;
                                if (myAppearanceAsset != null)
                                {
                                    AppearanceAssetEditScope editScope = new AppearanceAssetEditScope(doc);
                                    Asset editableAsset = editScope.Start(newMaterial.AppearanceAssetId);
                                    AssetProperty genericDiffuseProperty = editableAsset.FindByName("generic_diffuse");
                                    AssetPropertyDoubleArray4d asset4d = genericDiffuseProperty as AssetPropertyDoubleArray4d;
                                    if (asset4d != null)
                                    {
                                        asset4d.SetValueAsColor(new Color(255, 0, 0));
                                        editScope.Commit(true);

                                        //pick my new material
                                        materialWallFinish = newMaterial;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            #endregion

            //Room name
            string roomTestName = "GYM 38";


            #region 1.1) Before: Load FamilySymbol || After: Create wall type as baseboard
            #region perhaps delete this            
            //            string baseBoardFamPath = @"D:\MiguelProjects\01.TailorBird\1. Revit API Plugins\1. GitLab\8. Common areas Baseboarding\Baseboard Family.rfa";
            //            Family baseboardFamily = null;
            //
            //            //Delete any baseboard instance (Family Type element)
            //            List<Element> instances = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_GenericModel).WhereElementIsNotElementType().Where(x => x.Name.ToLower().Contains("baseboard")).ToList();
            //            if (instances.Any())
            //            {
            //                foreach (Element el in instances)
            //                {
            //                    doc.Delete(el.Id);
            //                }
            //            }
            //            //delete any baseboard previous "Family element"
            //            var baseboardFam = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(x => x.Name.ToLower().Contains("baseboard"));
            //            if (baseboardFam != null)
            //            {
            //                FamilySymbol famSym = baseboardFam as FamilySymbol;
            //                if (famSym != null)
            //                {
            //                    doc.Delete(famSym.Family.Id);
            //                }
            //            }
            //
            //
            //            //load family baseboard
            //            doc.LoadFamily(baseBoardFamPath, new utilities.FamilyLoadSettings(), out baseboardFamily);
            //
            //
            //            //activate FamilySymbol
            //            FamilySymbol baseboardFamilySymbol = null;
            //
            //            if (baseboardFamily != null)
            //            {
            //                var symbolId = baseboardFamily.GetFamilySymbolIds().FirstOrDefault();
            //                if (symbolId != null)
            //                {
            //                    FamilySymbol symbol = doc.GetElement(symbolId) as FamilySymbol;
            //                    if (symbol != null)
            //                    {
            //                        symbol.Activate();
            //                        baseboardFamilySymbol = symbol;
            //                    }
            //                }
            //            }
            //
            #endregion

            #region duplicate wall type
            #endregion

            #region load parameter to indicate this is a baseboard wall
            #endregion

            #endregion

            #region 1.2). Change WallJunionType
            List<SpatialElement> allRoomsInModel = new FilteredElementCollector(doc).OfClass(typeof(SpatialElement)).WhereElementIsNotElementType().Cast<SpatialElement>().Where(x => x.Category.Name == "Rooms" && x.Location != null).ToList();
            if (allRoomsInModel.Any())
            {
                foreach (SpatialElement element in allRoomsInModel)
                {
                    SpatialElementGeometryCalculator cal = new SpatialElementGeometryCalculator(doc, sebOptions);

                    Room room = element as Room;
                    double roomWallFinishArea = 0;

                    if (room != null)
                    {
                        //Get Room Name
                        //                        RvParam roomNamePar = room.get_Parameter(BuiltInParameter.ROOM_NAME);
                        //						string roomName = string.Empty;
                        //						if (roomNamePar != null) {
                        //							roomName =  roomNamePar.AsString();
                        //						}
                        //                    	
                        //                    	if (roomName != roomTestName) {
                        //                    		continue;
                        //                    	}

                        //Get Room Level
                        Level level = room.Level;
                        if (analyzeLevels == true)
                        {
                            RvParam lvlPar = level.get_Parameter(BuiltInParameter.DATUM_TEXT);
                            if (lvlPar != null)
                            {
                                if (!lvlPar.AsString().Contains(levelInput))
                                {
                                    continue;
                                }
                            }
                        }


                        SpatialElementGeometryResults results1 = cal.CalculateSpatialElementGeometry(room);
                        Solid roomSolid1 = results1.GetGeometry();
                        foreach (Face face in roomSolid1.Faces)
                        {
                            IList<SpatialElementBoundarySubface> subFaceList = results1.GetBoundaryFaceInfo(face);
                            foreach (SpatialElementBoundarySubface subface in subFaceList)
                            {
                                if (subface.SubfaceType == SubfaceType.Side)
                                {
                                    Wall wall = doc.GetElement(subface.SpatialBoundaryElement.HostElementId) as Wall;
                                    if (wall != null)
                                    {
                                        LocationCurve locCurve = wall.Location as LocationCurve;
                                        if (locCurve != null)
                                        {
                                            locCurve.set_JoinType(0, JoinType.Miter);
                                            locCurve.set_JoinType(1, JoinType.Miter);
                                        }
                                    }
                                }
                            }

                        }
                        doc.Regenerate();

                    }
                }
            }
            #endregion


            #region 1.3) ALGORITHM          
            List<ElementId> deleteInstances = new List<ElementId>();


            //always do a toList() and dont let the collector be a IEnumerable!! because it wont let you make changes
            List<Element> rooms = new FilteredElementCollector(doc).OfClass(typeof(SpatialElement)).Where<Element>(e => e is Room).ToList();
            List<ElementId> roomsAnalyzed = new List<ElementId>();
            if (rooms.Any())
            {
                foreach (Room room in rooms)
                {
                    if (room != null && room.Location != null && room.Area != 0)
                    {
                        //Get Room Name
                        RvParam roomNamePar = room.get_Parameter(BuiltInParameter.ROOM_NAME);
                        string roomName = string.Empty;
                        if (roomNamePar != null)
                        {
                            roomName = roomNamePar.AsString();
                        }

                        //                    	if (roomName != roomTestName) {
                        //                    		continue;
                        //                    	}


                        //Get room Geometry
                        SpatialElementGeometryCalculator cal = new SpatialElementGeometryCalculator(doc, sebOptions);

                        SpatialElementGeometryResults results2 = cal.CalculateSpatialElementGeometry(room);
                        Solid roomSolid2 = results2.GetGeometry(); //GetSolid from spatial geometry
                        XYZ centroidRoom2 = roomSolid2.ComputeCentroid();

                        //Get Room Level
                        Level level = room.Level;
                        if (analyzeLevels == true)
                        {
                            RvParam lvlPar = level.get_Parameter(BuiltInParameter.DATUM_TEXT);
                            if (lvlPar != null)
                            {
                                if (!lvlPar.AsString().Contains(levelInput))
                                {
                                    continue;
                                }
                            }
                        }

                        //wall areas
                        double widthExtrusion = 0.1;
                        double roomWallFinishArea = 0;

                        List<double> areas = new List<double>();
                        List<Line> theBottomLinesLoop = new List<Line>();



                        foreach (Face roomFace in roomSolid2.Faces)
                        {

                            // Filter spatial room Face by same wall Id from wall retrieved from bounding segment, instead of doing FaceNormal.Z != 1 ...etc.
                            IList<SpatialElementBoundarySubface> boundaryGeometryInfoList = results2.GetBoundaryFaceInfo(roomFace);//it will retrieve INFO for geometry boundary that define this specific spatial roomFace
                            foreach (SpatialElementBoundarySubface boundaryGeometryInfo in boundaryGeometryInfoList)//for each wall Info get Face info
                            {
                                if (boundaryGeometryInfo.SubfaceType == SubfaceType.Side) //Work with boundary geometry info that is a side face type
                                {
                                    //get element that gave rise to this geometry face <> wall face
                                    Wall wall = doc.GetElement(boundaryGeometryInfo.SpatialBoundaryElement.HostElementId) as Wall;


                                    if (wall != null)
                                    {
                                        //make sure we only analyze walls that base constraint at the same room level

                                        RvParam baseC = wall.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                                        if (baseC != null)
                                        {
                                            if (baseC.AsElementId() != level.Id)
                                            {
                                                continue;
                                            }
                                        }

                                        //for kitchen, there other wall that are being used as boundary walls and we are doubling counting them. So skip them
                                        if (wall.Name.ToLower().Contains("splash") || wall.Name.ToLower().Contains("tile"))
                                        {
                                            continue;
                                        }

                                        //Face of the bounding wall () 
                                        Face wallFace = boundaryGeometryInfo.GetBoundingElementFace();

                                        if (wallFace is PlanarFace)
                                        {
                                            //planarFace of the bounding wall
                                            PlanarFace planarWallFc = wallFace as PlanarFace;

                                            if (!areas.Contains(wallFace.Area))//I think I need more filter because it can happen there exist faces with same areas!! I might need to use face centroids
                                            {
                                                #region A) Baseboards                                            	
                                                #region A.i) Create Solid from opening inserts in each boundary wall
                                                IList<ElementId> wallInserts = wall.FindInserts(true, false, true, true);
                                                Solid overallOpeningSolid = null;
                                                if (wallInserts.Any())
                                                {
                                                    foreach (ElementId idinsert in wallInserts)
                                                    {
                                                        Element insert = doc.GetElement(idinsert);
                                                        FamilyInstance famIns = insert as FamilyInstance;
                                                        XYZ pCutDir = XYZ.Zero;
                                                        CurveLoop curveLoop = null;
                                                        //for famIns
                                                        if (famIns != null)
                                                        {
                                                            curveLoop = ExporterIFCUtils.GetInstanceCutoutFromWall(doc, wall, famIns, out pCutDir);

                                                        }
                                                        //it's an openning
                                                        else if (famIns == null)
                                                        {
                                                            Opening opening = insert as Opening;
                                                            if (opening != null)
                                                            {
                                                                curveLoop = tailorBird.getCurveLoopFromOpennings(opening);
                                                                pCutDir = planarWallFc.FaceNormal;

                                                            }
                                                        }

                                                        //CurveLoop curveLoop = ExporterIFCUtils.GetInstanceCutoutFromWall(doc, wall, famIns, out pCutDir);
                                                        /// ExporterIFCUtils.ComputeAreaOfCurveLoops( loops )


                                                        IList<CurveLoop> profileLoops = new List<CurveLoop>();
                                                        profileLoops.Add(curveLoop);
                                                        if (overallOpeningSolid == null)
                                                        {
                                                            Solid negExt = GeometryCreationUtilities.CreateExtrusionGeometry(profileLoops, -pCutDir, skirtingboardWidth * 10);//value was "1" feet, and skirtingboard width was "0.1" feet
                                                            Solid posExtr = GeometryCreationUtilities.CreateExtrusionGeometry(profileLoops, pCutDir, skirtingboardWidth * 10);
                                                            overallOpeningSolid = BooleanOperationsUtils.ExecuteBooleanOperation(negExt, posExtr, BooleanOperationsType.Union);

                                                        }
                                                        else if (overallOpeningSolid != null)
                                                        {
                                                            Solid negExt = GeometryCreationUtilities.CreateExtrusionGeometry(profileLoops, -pCutDir, skirtingboardWidth * 10);
                                                            Solid posExtr = GeometryCreationUtilities.CreateExtrusionGeometry(profileLoops, pCutDir, skirtingboardWidth * 10);
                                                            Solid openingSolid = BooleanOperationsUtils.ExecuteBooleanOperation(negExt, posExtr, BooleanOperationsType.Union);
                                                            overallOpeningSolid = BooleanOperationsUtils.ExecuteBooleanOperation(overallOpeningSolid, openingSolid, BooleanOperationsType.Union);
                                                        }
                                                    }
                                                }
                                                #endregion

                                                #region A.ii) Get the full SkirtingBoard solid
                                                Solid miniBaseboard = tailorBird.createSkirtingBoardSolidFromWall(planarWallFc, roomSolid2, skirtingboardHeight, skirtingboardWidth);

                                                if (miniBaseboard == null)
                                                {
                                                    
                                                    //stb.AppendLine("Missing Baseboard between: " + room.Name + " & Wall Id: " + wall.Id.ToString());
                                                    missingBaseboard.Add(new Tuple<string, string>(room.Name, wall.Id.ToString()));
                                                    //cant draw the solid as the value is null
                                                    throwWarning = true;
                                                    areas.Add(wallFace.Area);
                                                    continue;

                                                }
                                                #endregion

                                                #region A.iii) Subtract all the solid openings from the SkirtingBoard solid
                                                Solid partialBaseBoard = null;
                                                if (overallOpeningSolid != null)
                                                {
                                                    partialBaseBoard = BooleanOperationsUtils.ExecuteBooleanOperation(miniBaseboard, overallOpeningSolid, BooleanOperationsType.Difference);
                                                }
                                                else if (overallOpeningSolid == null)
                                                {
                                                    partialBaseBoard = miniBaseboard;
                                                }


                                                #endregion

                                                #region A.iv) Subtract kitchen solid geometries using run-ups
                                                if (room.Name.ToLower().Contains("kitchen"))
                                                {
                                                    #region A) Subtract run-ups
                                                    //Find Splashes, baths, etc.
                                                    List<Element> runUps = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_EdgeSlab).WhereElementIsNotElementType().Where(x => x.Name.ToLower().Contains("run-up")).ToList();

                                                    if (runUps.Any())
                                                    {
                                                        //For each runUp in kitchen subtract it solid
                                                        foreach (Element runUp in runUps)
                                                        {
                                                            XYZ centroid = tailorBird.getCentroidFromElement(runUp);

                                                            //Check if runUp (slabEdge) is in kitchen
                                                            if (tailorBird.isElementInsideRoom(room, centroid))
                                                            {

                                                                //Get Splash Geometry
                                                                Solid runUpSolid = tailorBird.getSolidFromElement(runUp);
                                                                if (runUpSolid == null) { continue; }

                                                                //extrude solids for faces pointing to the downside
                                                                List<Solid> bottomFacesAsSolid = new List<Solid>();
                                                                foreach (Face face in runUpSolid.Faces)
                                                                {
                                                                    PlanarFace planarFC = face as PlanarFace;
                                                                    if (planarFC != null)
                                                                    {
                                                                        XYZ normalFC = planarFC.FaceNormal;
                                                                        if (normalFC.Z < -0.95) // sometime we do not have a flat -1 direction
                                                                        {
                                                                            Solid extrudedSol = GeometryCreationUtilities.CreateExtrusionGeometry(face.GetEdgesAsCurveLoops(), normalFC, 5); // 5 feets will be more than enough
                                                                            bottomFacesAsSolid.Add(extrudedSol);
                                                                        }
                                                                    }
                                                                }

                                                                //for each bottomSolid, create extruded solids from side faces except the ones that face to skirtingboard direction and then Blend everything together
                                                                if (bottomFacesAsSolid.Any())
                                                                {
                                                                    List<Solid> sideFacesAsSolid = new List<Solid>();
                                                                    foreach (Solid solid in bottomFacesAsSolid)
                                                                    {

                                                                        //Exclude the faces with the lowest area and extrude the rest of them
                                                                        List<Tuple<Face, double>> sideFaces = new List<Tuple<Face, double>>();
                                                                        foreach (Face face in solid.Faces)
                                                                        {
                                                                            PlanarFace planarFC = face as PlanarFace;
                                                                            if (Math.Abs(planarFC.FaceNormal.Z) < 0.95)
                                                                            {
                                                                                sideFaces.Add(new Tuple<Face, double>(face, face.Area));
                                                                            }
                                                                        }
                                                                        sideFaces = sideFaces.OrderBy(x => x.Item2).ToList();
                                                                        sideFaces.RemoveRange(0, 2);

                                                                        //extrude the filtered faces (set different width w.r.t baseboard with !!, 0.11 it's ok)
                                                                        foreach (Tuple<Face, double> tuple in sideFaces)
                                                                        {
                                                                            Face sideFace = tuple.Item1;
                                                                            PlanarFace planarFC = sideFace as PlanarFace;
                                                                            if (planarFC != null)
                                                                            {
                                                                                Solid sideFaceExtruded = GeometryCreationUtilities.CreateExtrusionGeometry(sideFace.GetEdgesAsCurveLoops(), planarFC.FaceNormal, skirtingboardWidth * 1.1);//it was 0.11 feets
                                                                                sideFacesAsSolid.Add(sideFaceExtruded);
                                                                            }
                                                                        }

                                                                    }
                                                                    //Join the extruded faces and the bottom runUp solid
                                                                    Solid kitchenRoomRunUps = null;
                                                                    foreach (Solid solid in bottomFacesAsSolid)
                                                                    {
                                                                        if (kitchenRoomRunUps == null)
                                                                        {
                                                                            kitchenRoomRunUps = solid;
                                                                        }
                                                                        else if (kitchenRoomRunUps != null)
                                                                        {
                                                                            kitchenRoomRunUps = BooleanOperationsUtils.ExecuteBooleanOperation(kitchenRoomRunUps, solid, BooleanOperationsType.Union);
                                                                        }
                                                                    }
                                                                    foreach (Solid solid in sideFacesAsSolid)
                                                                    {
                                                                        if (kitchenRoomRunUps == null)
                                                                        {
                                                                            kitchenRoomRunUps = solid;
                                                                        }
                                                                        else if (kitchenRoomRunUps != null)
                                                                        {
                                                                            kitchenRoomRunUps = BooleanOperationsUtils.ExecuteBooleanOperation(kitchenRoomRunUps, solid, BooleanOperationsType.Union);
                                                                        }
                                                                    }


                                                                    //Eliminate skirtingboards at runUps location
                                                                    partialBaseBoard = BooleanOperationsUtils.ExecuteBooleanOperation(partialBaseBoard, kitchenRoomRunUps, BooleanOperationsType.Difference);


                                                                }
                                                            }
                                                        }


                                                    }

                                                    #endregion
                                                }

                                                #endregion

                                                #region A.v)Subtract Bathroom solid geometries using splashes(I'm considering a set of rules when subtractions gives me a Solid with 0 volume)
                                                if (room.Name.ToLower().Contains("bath"))
                                                {
                                                    #region a). Subtract splash solids
                                                    //Find Splashes, baths, etc.
                                                    List<Element> splashes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_EdgeSlab).WhereElementIsNotElementType().Where(x => x.Name.ToLower().Contains("splash")).ToList();

                                                    if (splashes.Any())
                                                    {
                                                        //For each splash in bath subtract it solid
                                                        foreach (Element splash in splashes)
                                                        {
                                                            XYZ centroid = tailorBird.getCentroidFromElement(splash);

                                                            //Check if Splash (slabEdge) is in bath
                                                            if (tailorBird.isElementInsideRoom(room, centroid))
                                                            {

                                                                //Get Splash Geometry
                                                                Solid splashSolid = tailorBird.getSolidFromElement(splash);
                                                                if (splashSolid == null) { continue; }


                                                                //extrude solids for faces pointing to the downside
                                                                List<Solid> bottomFacesAsSolid = new List<Solid>();
                                                                List<Solid> sideFacesAsSolid = new List<Solid>();
                                                                foreach (Face face in splashSolid.Faces)
                                                                {
                                                                    PlanarFace planarFC = face as PlanarFace;
                                                                    if (planarFC != null)
                                                                    {
                                                                        XYZ normalFC = planarFC.FaceNormal;
                                                                        if (normalFC.Z < -0.98)//sometimes faces do not have a flat -1 value!!
                                                                        {
                                                                            Solid extrudedSol = GeometryCreationUtilities.CreateExtrusionGeometry(face.GetEdgesAsCurveLoops(), normalFC, 5); // 5 feets will be more than enough
                                                                            bottomFacesAsSolid.Add(extrudedSol);
                                                                        }
                                                                    }
                                                                }

                                                                //for each bottomSolid, create extruded solids from side faces except the ones that face to skirtingboard direction and then Blend everything together
                                                                if (bottomFacesAsSolid.Any())
                                                                {
                                                                    foreach (Solid solid in bottomFacesAsSolid)
                                                                    {

                                                                        //Exclude the faces with the lowest area and extrude the rest of them
                                                                        List<Tuple<Face, double>> sideFaces = new List<Tuple<Face, double>>();
                                                                        foreach (Face face in solid.Faces)
                                                                        {
                                                                            PlanarFace planarFC = face as PlanarFace;
                                                                            if (Math.Abs(planarFC.FaceNormal.Z) <= 0.95)
                                                                            {
                                                                                sideFaces.Add(new Tuple<Face, double>(face, face.Area));
                                                                            }
                                                                        }
                                                                        sideFaces = sideFaces.OrderBy(x => x.Item2).ToList();
                                                                        sideFaces.RemoveRange(0, 2);

                                                                        //extrude the filtered faces (different width w.r.t baseboard width !!)
                                                                        foreach (Tuple<Face, double> tuple in sideFaces)
                                                                        {
                                                                            Face sideFace = tuple.Item1;
                                                                            PlanarFace planarFC = sideFace as PlanarFace;
                                                                            if (planarFC != null)
                                                                            {
                                                                                Solid sideFaceExtruded = GeometryCreationUtilities.CreateExtrusionGeometry(sideFace.GetEdgesAsCurveLoops(), planarFC.FaceNormal, skirtingboardWidth * 1.2);//it was 0.11 feets
                                                                                sideFacesAsSolid.Add(sideFaceExtruded);
                                                                            }
                                                                        }

                                                                    }
                                                                    //Join the extruded faces and the bottom splash solid
                                                                    Solid bathroomSplashes = null;
                                                                    foreach (Solid solid in bottomFacesAsSolid)
                                                                    {
                                                                        if (bathroomSplashes == null)
                                                                        {
                                                                            bathroomSplashes = solid;
                                                                        }
                                                                        else if (bathroomSplashes != null)
                                                                        {
                                                                            bathroomSplashes = BooleanOperationsUtils.ExecuteBooleanOperation(bathroomSplashes, solid, BooleanOperationsType.Union);
                                                                        }
                                                                    }
                                                                    foreach (Solid solid in sideFacesAsSolid)
                                                                    {
                                                                        if (bathroomSplashes == null)
                                                                        {
                                                                            bathroomSplashes = solid;
                                                                        }
                                                                        else if (bathroomSplashes != null)
                                                                        {
                                                                            bathroomSplashes = BooleanOperationsUtils.ExecuteBooleanOperation(bathroomSplashes, solid, BooleanOperationsType.Union);
                                                                        }
                                                                    }

                                                                    //Eliminate skirtingboards at splashes location
                                                                    partialBaseBoard = BooleanOperationsUtils.ExecuteBooleanOperation(partialBaseBoard, bathroomSplashes, BooleanOperationsType.Difference);

                                                                }
                                                            }
                                                        }
                                                    }
                                                    #endregion

                                                    #region b) Subtract baths solid
                                                    List<FamilyInstance> baths = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Where(x => x.Name.ToLower().Contains("tub") && (x.Name.ToLower().Contains("garden") || x.Name.ToLower().Contains("standard"))).ToList();
                                                    if (baths.Any())
                                                    {
                                                        foreach (FamilyInstance bath in baths)
                                                        {
                                                            if (bath.Room.Name.ToLower().Contains("bath"))
                                                            {

                                                                Solid bathSolid = tailorBird.getSolidFromElement(bath);
                                                                if (bathSolid == null)
                                                                {
                                                                    continue;
                                                                }
                                                                partialBaseBoard = BooleanOperationsUtils.ExecuteBooleanOperation(partialBaseBoard, bathSolid, BooleanOperationsType.Difference); //IS THERE A PROBLEM IF THE SECOND SOLID ELIMINATES COMPLETELY THE FIRST SOLID??
                                                            }
                                                        }
                                                    }

                                                    #endregion

                                                    #region c) Subtract ShowerTiles
                                                    List<FamilyInstance> showerTiles = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Where(x => x.Name.ToLower().Contains("shower") && x.Name.ToLower().Contains("tile")).ToList();

                                                    if (showerTiles.Any())
                                                    {
                                                        foreach (FamilyInstance showerTile in showerTiles)
                                                        {
                                                            //if you query the room from ShowerTile, it does not work sometimes, really wear!! I think because of the phase!!

                                                            //Retrieve shower tile solid
                                                            Solid showerTileSolid = tailorBird.getSolidFromElement(showerTile);
                                                            if (showerTileSolid == null) { continue; }

                                                            //extrude the side faces with largest areas
                                                            List<Tuple<PlanarFace, double>> sideFaces = new List<Tuple<PlanarFace, double>>();
                                                            foreach (Face face in showerTileSolid.Faces)
                                                            {
                                                                PlanarFace planarFC = face as PlanarFace;
                                                                if (planarFC != null)
                                                                {
                                                                    if (planarFC.FaceNormal.Z != 1 && planarFC.FaceNormal.Z != -1)
                                                                    {
                                                                        sideFaces.Add(new Tuple<PlanarFace, double>(planarFC, face.Area));
                                                                    }
                                                                }
                                                            }
                                                            sideFaces = sideFaces.OrderBy(x => x.Item2).ToList();
                                                            sideFaces.RemoveRange(0, 2);

                                                            //join all the extrussions ( to the original showerTile solid)
                                                            foreach (Tuple<PlanarFace, double> tuple in sideFaces)
                                                            {
                                                                PlanarFace sideFace = tuple.Item1;
                                                                Solid faceExtrusion = GeometryCreationUtilities.CreateExtrusionGeometry(sideFace.GetEdgesAsCurveLoops(), sideFace.FaceNormal, skirtingboardWidth * 3);//0.3 original value
                                                                showerTileSolid = BooleanOperationsUtils.ExecuteBooleanOperation(showerTileSolid, faceExtrusion, BooleanOperationsType.Union);

                                                            }

                                                            //shower Tile does not reach the bottom floor, so extrude the bottom face for the final solid
                                                            foreach (Face fc in showerTileSolid.Faces)
                                                            {
                                                                PlanarFace plFc = fc as PlanarFace;
                                                                if (plFc != null)
                                                                {
                                                                    if (plFc.FaceNormal.Z == -1)
                                                                    {
                                                                        Solid bottomfcSolid = GeometryCreationUtilities.CreateExtrusionGeometry(plFc.GetEdgesAsCurveLoops(), plFc.FaceNormal, skirtingboardWidth * 2); //0.2 original value
                                                                        showerTileSolid = BooleanOperationsUtils.ExecuteBooleanOperation(showerTileSolid, bottomfcSolid, BooleanOperationsType.Union);
                                                                    }
                                                                }
                                                            }
                                                            //DirectShape ds = DirectShape.CreateElement(doc,new ElementId(BuiltInCategory.OST_GenericModel));
                                                            //ds.SetShape(new List<GeometryObject>{ showerTileSolid as GeometryObject});

                                                            //subtract solids
                                                            partialBaseBoard = BooleanOperationsUtils.ExecuteBooleanOperation(partialBaseBoard, showerTileSolid, BooleanOperationsType.Difference);
                                                        }
                                                    }
                                                    #endregion

                                                }

                                                #endregion

                                                #region A.vi). For Stairs do not place baseboards below the biggest hrz stair face
                                                List<Element> stairs = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Stairs).WhereElementIsNotElementType().ToList();
                                                if (stairs.Any())
                                                {
                                                    List<Tuple<double, PlanarFace>> bottomsFaces = new List<Tuple<double, PlanarFace>>();

                                                    foreach (Element stair in stairs)
                                                    {
                                                        BoundingBoxXYZ bbStair = stair.get_BoundingBox(null);
                                                        if (bbStair == null)
                                                        {
                                                            continue;
                                                        }

                                                        if (tailorBird.isElementInsideRoom(room, (bbStair.Min + bbStair.Max) * 0.5) == false)
                                                        {
                                                            continue;
                                                        }


                                                        GeometryElement geo = stair.get_Geometry(new Options());
                                                        //I'm assuming if solid appears, geometry instance is fake!
                                                        if (geo.Any(x => x is Solid)) //Watch out, x as Solid can return Null!! How do I assure solid has a volume then?
                                                        {
                                                            foreach (GeometryObject geomObj in geo)
                                                            {
                                                                if (geomObj is Solid)
                                                                {
                                                                    Solid solidGeo = geomObj as Solid;
                                                                    if (null != solidGeo)
                                                                    {
                                                                        if (solidGeo.Volume > 0.01)
                                                                        {

                                                                            foreach (Face face in solidGeo.Faces)
                                                                            {
                                                                                PlanarFace plFc = face as PlanarFace;
                                                                                if (plFc != null)
                                                                                {
                                                                                    if (plFc.FaceNormal.Z <= -0.95)
                                                                                    {
                                                                                        bottomsFaces.Add(new Tuple<double, PlanarFace>(plFc.Area, plFc));
                                                                                    }
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                        }
                                                        else
                                                        {
                                                            foreach (GeometryObject geomObj in geo)
                                                            {
                                                                if (geomObj is GeometryInstance)
                                                                {
                                                                    GeometryInstance instance = geomObj as GeometryInstance;
                                                                    if (null != instance)
                                                                    {
                                                                        GeometryElement geoElement = instance.GetInstanceGeometry();
                                                                        foreach (GeometryObject geomEl in geoElement)
                                                                        {
                                                                            if (geomEl is Solid)
                                                                            {
                                                                                Solid solidGeo = geomEl as Solid;
                                                                                if (solidGeo != null)
                                                                                {
                                                                                    if (solidGeo.Volume > 0.01)
                                                                                    {

                                                                                        foreach (Face face in solidGeo.Faces)
                                                                                        {
                                                                                            PlanarFace plFc = face as PlanarFace;
                                                                                            if (plFc != null)
                                                                                            {
                                                                                                if (plFc.FaceNormal.Z <= -0.95)
                                                                                                {
                                                                                                    bottomsFaces.Add(new Tuple<double, PlanarFace>(plFc.Area, plFc));
                                                                                                }
                                                                                            }
                                                                                        }
                                                                                    }
                                                                                }
                                                                            }
                                                                        }

                                                                    }
                                                                }
                                                            }
                                                        }
                                                        bottomsFaces = bottomsFaces.OrderByDescending(x => x.Item1).ToList();
                                                        Solid stairFaceExtruded = GeometryCreationUtilities.CreateExtrusionGeometry(bottomsFaces[0].Item2.GetEdgesAsCurveLoops(), bottomsFaces[0].Item2.FaceNormal, 10);
                                                        //Extrude lower side faces
                                                        List<Tuple<double, PlanarFace>> sideFaces = new List<Tuple<double, PlanarFace>>();
                                                        foreach (Face face in stairFaceExtruded.Faces)
                                                        {
                                                            PlanarFace plFC = face as PlanarFace;
                                                            if (Math.Abs(plFC.FaceNormal.Z) <= 0.05)
                                                            {
                                                                sideFaces.Add(new Tuple<double, PlanarFace>(plFC.Area, plFC));
                                                            }
                                                        }
                                                        sideFaces = sideFaces.OrderByDescending(x => x.Item1).ToList();

                                                        Solid sideLargestFaceExtruded = GeometryCreationUtilities.CreateExtrusionGeometry(sideFaces[0].Item2.GetEdgesAsCurveLoops(), sideFaces[0].Item2.FaceNormal, 0.5);
                                                        stairFaceExtruded = BooleanOperationsUtils.ExecuteBooleanOperation(stairFaceExtruded, sideLargestFaceExtruded, BooleanOperationsType.Union);

                                                        //find other side faces that do not point to the largestFace direction

                                                        foreach (Face face in stairFaceExtruded.Faces)
                                                        {
                                                            PlanarFace plFC = face as PlanarFace;
                                                            if (Math.Abs(plFC.FaceNormal.Z) <= 0.05)
                                                            {
                                                                if (plFC.FaceNormal.IsAlmostEqualTo(sideFaces[0].Item2.FaceNormal) == false || plFC.FaceNormal.IsAlmostEqualTo(-sideFaces[0].Item2.FaceNormal))
                                                                {
                                                                    if (plFC.Area > 5)
                                                                    {
                                                                        Solid sideFaceSolid = GeometryCreationUtilities.CreateExtrusionGeometry(face.GetEdgesAsCurveLoops(), plFC.FaceNormal, 0.5);
                                                                        stairFaceExtruded = BooleanOperationsUtils.ExecuteBooleanOperation(stairFaceExtruded, sideFaceSolid, BooleanOperationsType.Union);
                                                                    }
                                                                }
                                                            }
                                                        }


                                                        partialBaseBoard = BooleanOperationsUtils.ExecuteBooleanOperation(partialBaseBoard, stairFaceExtruded, BooleanOperationsType.Difference);

                                                        //DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                                                        //ds.SetShape(new List<GeometryObject> { stairFaceExtruded as GeometryObject });
                                                    }
                                                }


                                                #endregion

                                                //DirectShape ds = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                                                //ds.SetShape(new List<GeometryObject> { partialBaseBoard as GeometryObject });

                                                //Kitche placing wear familysymbol besides there is no geometry!!

                                                #region A.vi) PLACE baseboards                                           

                                                foreach (Face face in partialBaseBoard.Faces)
                                                {
                                                    PlanarFace planarFace = face as PlanarFace;
                                                    if (planarFace != null)
                                                    {
                                                        if (planarWallFc.FaceNormal.IsAlmostEqualTo(-planarFace.FaceNormal))//-planarFace.FaceNormal: colelc the face adjacent to the wall
                                                        {
                                                            //tailorBird.tessellateFace(face,doc);

                                                            List<XYZ> pts = new List<XYZ>();

                                                            //i). get max points from hrz lines
                                                            List<double> zVal = new List<double>();
                                                            foreach (CurveLoop cvLoop in face.GetEdgesAsCurveLoops())
                                                            {
                                                                foreach (Curve cv in cvLoop)
                                                                {
                                                                    Line line = cv as Line;
                                                                    if (line != null)
                                                                    {
                                                                        if (-0.05 < Math.Abs(line.Direction.Z) && Math.Abs(line.Direction.Z) < 0.05)
                                                                        {
                                                                            //stb.AppendLine(wall.Id.ToString() +" ; "+ line.Direction.Z.ToString());
                                                                            zVal.Add(line.GetEndPoint(0).Z);
                                                                            zVal.Add(line.GetEndPoint(1).Z);
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                            //stb.AppendLine(zVal.Count.ToString() +" ; "+ zVal.Min().ToString());

                                                            //ii).Get hrz lines from faces
                                                            List<Line> lines = new List<Line>();
                                                            foreach (CurveLoop cvLoop in face.GetEdgesAsCurveLoops())
                                                            {
                                                                foreach (Curve cv in cvLoop)
                                                                {
                                                                    Line line = cv as Line;
                                                                    if (line != null)
                                                                    {
                                                                        if (-0.05 < Math.Abs(line.Direction.Z) && Math.Abs(line.Direction.Z) < 0.05)
                                                                        {

                                                                            if (line.GetEndPoint(0).Z < zVal.Max() && line.GetEndPoint(1).Z < zVal.Max())
                                                                            {
                                                                                //stb.AppendLine(line.GetEndPoint(0).Z.ToString() +" ; "+ line.GetEndPoint(1).Z.ToString());
                                                                                lines.Add(line);
                                                                            }
                                                                        }
                                                                    }
                                                                }
                                                            }

                                                            if (lines.Any())
                                                            {
                                                                //theBottomLinesLoop.Add(lines[0]); //for a 1 face, stb is telling us that there is only 1 bottom line, which makes sense //false! if there are multiple opennings along a wall like in a hallway
                                                                //ElementId wallId = doc.GetDefaultFamilyTypeId(new ElementId(BuiltInCategory.OST_Walls));
                                                                

                                                                foreach (Line line in lines)
                                                                {
                                                                    theBottomLinesLoop.Add(line);

                                                                    Curve theLine = line.CreateOffset(0.05, -XYZ.BasisZ);
                                                                    //create walls (hallway floor 2, double walls at the opposite site, why?)
                                                                    //living room 1, floor 1
                                                                    if (wallType != null)
                                                                    {
                                                                        Wall wallBaseboard = Wall.Create(doc, theLine, wallType.Id, level.Id, 0.4, 0, false, false);
                                                                        if (wallBaseboard != null)
                                                                        {
                                                                            RvParam boundingR = wallBaseboard.get_Parameter(BuiltInParameter.WALL_ATTR_ROOM_BOUNDING);
                                                                            if (boundingR != null)
                                                                            {
                                                                                boundingR.Set(0);
                                                                            }

                                                                            RvParam locWall = wallBaseboard.LookupParameter("Location");
                                                                            if (locWall != null && roomName != string.Empty)
                                                                            {
                                                                                locWall.Set(roomName);
                                                                            }
                                                                        }
                                                                    }


                                                                }


                                                                //CurveLoop cvL = CurveLoop.Create(new List<Curve>{Line.CreateBound(new XYZ(-18,137.72,9.43),new XYZ(-18,132.43,9.43))});
                                                                //Railing rail = Railing.Create(doc,cvL,railType.Id,level.Id);

                                                                //                                                    			FamilyInstance instanceBaseboard = doc.Create.NewFamilyInstance(lines[0], baseboardFamilySymbol, level, StructuralType.NonStructural);
                                                                //                                                    			doc.Regenerate();//regenerate so the facing orientation has a real value!
                                                                //                                                    			if (planarWallFc.FaceNormal.IsAlmostEqualTo(instanceBaseboard.FacingOrientation)) {
                                                                //                                                    				                                                    				
                                                                //	                                                    		}
                                                                //	                                                    		else{
                                                                //	                                                    			deleteInstances.Add(instanceBaseboard.Id);	                                                    			
                                                                //	                                                    			FamilyInstance instanceBase2 = doc.Create.NewFamilyInstance(lines[0].CreateReversed(), baseboardFamilySymbol, level, StructuralType.NonStructural);
                                                                //	                                                    		}
                                                            }
                                                        }
                                                    }
                                                }
                                                #endregion

                                                #endregion

                                                #region B) Wall Areas
                                                //B.i) its the similar from step a.i)                                                

                                                #region B.ii). Create solid from bondary wall face
                                                Solid wallFaceExtrus = null;
                                                try
                                                {
                                                    //The profile CurveLoops do not satisfy the input requirements                                                    
                                                    wallFaceExtrus = GeometryCreationUtilities.CreateExtrusionGeometry(planarWallFc.GetEdgesAsCurveLoops(), planarWallFc.FaceNormal, widthExtrusion); //0.1 original value
                                                }
                                                catch {
                                                    //stb.AppendLine("Missing Wall Finish between" + room.Name + " &  Wall Id: " + wall.Id.ToString());
                                                    missingWallFinish.Add(new Tuple<string, string>(room.Name, wall.Id.ToString()));
                                                    throwWarning = true;
                                                    areas.Add(wallFace.Area);
                                                    continue;
                                                }

                                                //make sure the wallFaceExtruded it's surrounding the room (roomSolid is produced by wall faces, so they have same height, although width is variable that's why we intersect both solids)
                                                try
                                                {
                                                    wallFaceExtrus = BooleanOperationsUtils.ExecuteBooleanOperation(roomSolid2, wallFaceExtrus, BooleanOperationsType.Intersect);
                                                }
                                                catch
                                                {
                                                    //DirectShape ds3 = DirectShape.CreateElement(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                                                    //ds3.SetShape(new List<GeometryObject> { wallFaceExtrus as GeometryObject });
                                                    //stb.AppendLine("Missing Wall Finish between" + room.Name + " &  Wall Id: " + wall.Id.ToString());
                                                    missingWallFinish.Add(new Tuple<string, string>(room.Name, wall.Id.ToString()));
                                                    throwWarning = true;
                                                    areas.Add(wallFace.Area);
                                                    continue;
                                                }

                                                #endregion

                                                #region B.iii). subtract openings from wall face solid
                                                if (overallOpeningSolid != null)
                                                {
                                                    wallFaceExtrus = BooleanOperationsUtils.ExecuteBooleanOperation(wallFaceExtrus, overallOpeningSolid, BooleanOperationsType.Difference);
                                                }

                                                #endregion

                                                #region B.iv). Subtract bathroom solid geometries
                                                if (room.Name.ToLower().Contains("bath"))
                                                {

                                                    #region a). Subtract solids only below vanity splashes
                                                    //i). Filter vanity splashes
                                                    List<Element> vanitySplashes = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_EdgeSlab).WhereElementIsNotElementType().Where(x => x.Name.ToLower().Contains("splash") && x.Name.ToLower().Contains("vanity")).ToList();

                                                    //For each splash in bath subtract it solid
                                                    foreach (Element splash in vanitySplashes)
                                                    {
                                                        XYZ centroid = tailorBird.getCentroidFromElement(splash);

                                                        //ii). Check if Splash (slabEdge) is in bath
                                                        if (tailorBird.isElementInsideRoom(room, centroid))
                                                        {

                                                            //iii). Get Splash Geometry
                                                            Solid splashSolid = tailorBird.getSolidFromElement(splash);
                                                            if (splashSolid == null)
                                                            {
                                                                continue;
                                                            }

                                                            //iv). extrude solids for the face that points to the downside (there can be 2 or more bottom solids)
                                                            List<Solid> bottomFacesAsSolid = new List<Solid>();
                                                            List<Solid> sideFacesAsSolid = new List<Solid>();

                                                            foreach (Face face in splashSolid.Faces)
                                                            {
                                                                PlanarFace planarFC = face as PlanarFace;
                                                                if (planarFC != null)
                                                                {
                                                                    XYZ normalFC = planarFC.FaceNormal;
                                                                    if (normalFC.Z == -1)
                                                                    {
                                                                        Solid extrudedSol = GeometryCreationUtilities.CreateExtrusionGeometry(face.GetEdgesAsCurveLoops(), normalFC, 5); // 5 feets will be more than enough
                                                                        bottomFacesAsSolid.Add(extrudedSol);
                                                                    }
                                                                }
                                                            }

                                                            //v). for each bottomSolid, create extruded solids from side faces except the 2 faces with the lowest area and then Blend everything together
                                                            if (bottomFacesAsSolid.Any())
                                                            {
                                                                foreach (Solid solid in bottomFacesAsSolid)
                                                                {
                                                                    List<Tuple<Face, double>> sideFaces = new List<Tuple<Face, double>>();
                                                                    foreach (Face face in solid.Faces)
                                                                    {
                                                                        PlanarFace planarFC = face as PlanarFace;
                                                                        if (planarFC.FaceNormal.Z != 1 && planarFC.FaceNormal.Z != -1)
                                                                        {
                                                                            sideFaces.Add(new Tuple<Face, double>(face, face.Area));
                                                                        }
                                                                    }
                                                                    sideFaces = sideFaces.OrderBy(x => x.Item2).ToList();
                                                                    sideFaces.RemoveRange(0, 2);

                                                                    //extrude the filtered faces (different width w.r.t baseboard width !!)
                                                                    foreach (Tuple<Face, double> tuple in sideFaces)
                                                                    {
                                                                        Face sideFace = tuple.Item1;
                                                                        PlanarFace planarFC = sideFace as PlanarFace;
                                                                        if (planarFC != null)
                                                                        {
                                                                            Solid sideFaceExtruded = GeometryCreationUtilities.CreateExtrusionGeometry(sideFace.GetEdgesAsCurveLoops(), planarFC.FaceNormal, widthExtrusion * 1.5);//it was 0.15 feets
                                                                            sideFacesAsSolid.Add(sideFaceExtruded);
                                                                        }
                                                                    }
                                                                }
                                                                //vi). Join the extruded faces and the bottom splash solids
                                                                Solid bathroomSplashes = null;
                                                                foreach (Solid solid in bottomFacesAsSolid)
                                                                {
                                                                    if (bathroomSplashes == null)
                                                                    {
                                                                        bathroomSplashes = solid;
                                                                    }
                                                                    else if (bathroomSplashes != null)
                                                                    {
                                                                        bathroomSplashes = BooleanOperationsUtils.ExecuteBooleanOperation(bathroomSplashes, solid, BooleanOperationsType.Union);
                                                                    }
                                                                }

                                                                foreach (Solid solid in sideFacesAsSolid)
                                                                {
                                                                    if (bathroomSplashes == null)
                                                                    {
                                                                        bathroomSplashes = solid;
                                                                    }
                                                                    else if (bathroomSplashes != null)
                                                                    {
                                                                        bathroomSplashes = BooleanOperationsUtils.ExecuteBooleanOperation(bathroomSplashes, solid, BooleanOperationsType.Union);
                                                                    }
                                                                }

                                                                //vii). Eliminate solid areas from wallFaceExtrus below vanity splashes
                                                                wallFaceExtrus = BooleanOperationsUtils.ExecuteBooleanOperation(wallFaceExtrus, bathroomSplashes, BooleanOperationsType.Difference);
                                                            }
                                                        }
                                                    }
                                                    #endregion

                                                    #region b) Subtract baths/showers: the whole family do not retrieve the overall geometry so we need to get geometry for each independent element

                                                    #region b.1) Subtract garden/Standard tubs Solid
                                                    List<FamilyInstance> tubs = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Where(x => x.Name.ToLower().Contains("tub") && (x.Name.ToLower().Contains("garden") || x.Name.ToLower().Contains("standard"))).ToList();
                                                    if (tubs.Any())
                                                    {
                                                        foreach (FamilyInstance tub in tubs)
                                                        {
                                                            //make sure tubs are placed in baths
                                                            if (tub.Room.Name.ToLower().Contains("bath"))
                                                            {

                                                                Solid bathSolid = tailorBird.getSolidFromElement(tub); //only retrieving geometry of the bath
                                                                if (bathSolid != null)
                                                                {

                                                                    try
                                                                    {
                                                                        wallFaceExtrus = BooleanOperationsUtils.ExecuteBooleanOperation(wallFaceExtrus, bathSolid, BooleanOperationsType.Difference);
                                                                    }
                                                                    catch
                                                                    {
                                                                        TaskDialog.Show("Revit exception", "TBR-01: Tub garden/standard is not properly aligned to the bath wall");
                                                                        tr.RollBack();
                                                                        return Result.Failed;//Result.Failed;
                                                                    }
                                                                    //through exception, tub is not aligned correctly to the walls!!
                                                                }

                                                            }
                                                        }
                                                    }
                                                    #endregion

                                                    #region b.2) Subtract Shower tiles & Bath tiles/surround solid

                                                    #region b.2.1) ShowerTiles/Surrounds
                                                    List<FamilyInstance> showerTilesAndSurround = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Where(x => x.Name.ToLower().Contains("shower") && (x.Name.ToLower().Contains("tile") || x.Name.ToLower().Contains("surround"))).ToList();

                                                    if (showerTilesAndSurround.Any())
                                                    {
                                                        foreach (FamilyInstance showerTile in showerTilesAndSurround)
                                                        {
                                                            if (showerTile.Room.Name.ToLower().Contains("bath"))
                                                            {
                                                                //Retrieve shower tile solid
                                                                Solid showerTileSolid = tailorBird.getSolidFromElement(showerTile);

                                                                if (showerTileSolid == null)
                                                                {
                                                                    continue;
                                                                }

                                                                //extrude the side faces with largest areas
                                                                List<Tuple<PlanarFace, double>> sideFaces = new List<Tuple<PlanarFace, double>>();
                                                                foreach (Face face in showerTileSolid.Faces)
                                                                {
                                                                    PlanarFace planarFC = face as PlanarFace;
                                                                    if (planarFC != null)
                                                                    {
                                                                        if (planarFC.FaceNormal.Z != 1 && planarFC.FaceNormal.Z != -1)
                                                                        {
                                                                            sideFaces.Add(new Tuple<PlanarFace, double>(planarFC, face.Area));
                                                                        }
                                                                    }
                                                                }
                                                                sideFaces = sideFaces.OrderBy(x => x.Item2).ToList();
                                                                sideFaces.RemoveRange(0, 2);

                                                                //join all the extrussions
                                                                foreach (Tuple<PlanarFace, double> tuple in sideFaces)
                                                                {
                                                                    PlanarFace sideFace = tuple.Item1;
                                                                    Solid faceExtrusion = GeometryCreationUtilities.CreateExtrusionGeometry(sideFace.GetEdgesAsCurveLoops(), sideFace.FaceNormal, widthExtrusion * 1.5);//0.1 original value
                                                                    showerTileSolid = BooleanOperationsUtils.ExecuteBooleanOperation(showerTileSolid, faceExtrusion, BooleanOperationsType.Union);
                                                                }

                                                                //subtract solids
                                                                wallFaceExtrus = BooleanOperationsUtils.ExecuteBooleanOperation(wallFaceExtrus, showerTileSolid, BooleanOperationsType.Difference);
                                                            }
                                                        }
                                                    }
                                                    #endregion

                                                    #region b.2.2) Bath tiles/surround solid
                                                    List<FamilyInstance> bathSurroundTile = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Where(x => x.Name.ToLower().Contains("bath") && (x.Name.ToLower().Contains("surround") || x.Name.ToLower().Contains("tile"))).ToList();

                                                    if (bathSurroundTile.Any())
                                                    {
                                                        foreach (FamilyInstance bathST in bathSurroundTile)
                                                        {
                                                            if (bathST.Room.Name.ToLower().Contains("bath"))
                                                            {
                                                                //Retrieve bath tile/surround solid
                                                                Solid bathSurroundTileSolid = tailorBird.getSolidFromElement(bathST);
                                                                if (bathSurroundTileSolid == null)
                                                                {
                                                                    continue;
                                                                }

                                                                //extrude the side faces with largest areas
                                                                List<Tuple<PlanarFace, double>> sideFaces = new List<Tuple<PlanarFace, double>>();
                                                                foreach (Face face in bathSurroundTileSolid.Faces)
                                                                {
                                                                    PlanarFace planarFC = face as PlanarFace;
                                                                    if (planarFC != null)
                                                                    {
                                                                        if (planarFC.FaceNormal.Z != 1 && planarFC.FaceNormal.Z != -1)
                                                                        {
                                                                            sideFaces.Add(new Tuple<PlanarFace, double>(planarFC, face.Area));
                                                                        }
                                                                    }
                                                                }
                                                                sideFaces = sideFaces.OrderBy(x => x.Item2).ToList();
                                                                sideFaces.RemoveRange(0, 2);

                                                                //join all the extrussions
                                                                foreach (Tuple<PlanarFace, double> tuple in sideFaces)
                                                                {
                                                                    PlanarFace sideFace = tuple.Item1;
                                                                    Solid faceExtrusion = GeometryCreationUtilities.CreateExtrusionGeometry(sideFace.GetEdgesAsCurveLoops(), sideFace.FaceNormal, widthExtrusion * 1.5); //0.1 original value
                                                                    bathSurroundTileSolid = BooleanOperationsUtils.ExecuteBooleanOperation(bathSurroundTileSolid, faceExtrusion, BooleanOperationsType.Union);
                                                                }

                                                                wallFaceExtrus = BooleanOperationsUtils.ExecuteBooleanOperation(wallFaceExtrus, bathSurroundTileSolid, BooleanOperationsType.Difference);
                                                            }
                                                        }
                                                    }
                                                    #endregion

                                                    #endregion

                                                    #endregion


                                                }


                                                #endregion

                                                #region B.v) subtract kitchen solid geometries
                                                if (room.Name.ToLower().Contains("kitchen"))
                                                {

                                                    #region a). subtract solids below run ups
                                                    List<Element> runUps = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_EdgeSlab).WhereElementIsNotElementType().Where(x => x.Name.ToLower().Contains("run-up")).ToList();
                                                    foreach (Element runUp in runUps)
                                                    {
                                                        XYZ centroid = tailorBird.getCentroidFromElement(runUp);
                                                        if (tailorBird.isElementInsideRoom(room, centroid))
                                                        {

                                                            Solid runupSolid = tailorBird.getSolidFromElement(runUp);
                                                            if (runupSolid == null) { continue; }

                                                            List<Solid> bottomFacesAsSolid = new List<Solid>();
                                                            List<Solid> sideFacesAsSolid = new List<Solid>();

                                                            foreach (Face face in runupSolid.Faces)
                                                            {
                                                                PlanarFace planarFC = face as PlanarFace;
                                                                if (planarFC != null)
                                                                {
                                                                    XYZ normalFC = planarFC.FaceNormal;
                                                                    if (normalFC.Z == -1)
                                                                    {
                                                                        Solid extrudedSol = GeometryCreationUtilities.CreateExtrusionGeometry(face.GetEdgesAsCurveLoops(), normalFC, 5); // 5 feets will be more than enough
                                                                        bottomFacesAsSolid.Add(extrudedSol);
                                                                    }
                                                                }
                                                            }

                                                            if (bottomFacesAsSolid.Any())
                                                            {
                                                                foreach (Solid solid in bottomFacesAsSolid)
                                                                {
                                                                    List<Tuple<Face, double>> sideFaces = new List<Tuple<Face, double>>();
                                                                    foreach (Face face in solid.Faces)
                                                                    {
                                                                        PlanarFace planarFC = face as PlanarFace;
                                                                        if (planarFC.FaceNormal.Z != 1 && planarFC.FaceNormal.Z != -1)
                                                                        {
                                                                            sideFaces.Add(new Tuple<Face, double>(face, face.Area));
                                                                        }
                                                                    }
                                                                    sideFaces = sideFaces.OrderBy(x => x.Item2).ToList();
                                                                    sideFaces.RemoveRange(0, 2);

                                                                    //extrude the filtered faces (different width w.r.t baseboard width !!)
                                                                    foreach (Tuple<Face, double> tuple in sideFaces)
                                                                    {
                                                                        Face sideFace = tuple.Item1;
                                                                        PlanarFace planarFC = sideFace as PlanarFace;
                                                                        if (planarFC != null)
                                                                        {
                                                                            Solid sideFaceExtruded = GeometryCreationUtilities.CreateExtrusionGeometry(sideFace.GetEdgesAsCurveLoops(), planarFC.FaceNormal, widthExtrusion * 1.5);//it was 0.11 feets
                                                                            sideFacesAsSolid.Add(sideFaceExtruded);
                                                                        }
                                                                    }
                                                                }
                                                                //vi). Join the extruded faces and the bottom splash solids
                                                                Solid kitchenRunups = null;
                                                                foreach (Solid solid in bottomFacesAsSolid)
                                                                {
                                                                    if (kitchenRunups == null)
                                                                    {
                                                                        kitchenRunups = solid;
                                                                    }
                                                                    else if (kitchenRunups != null)
                                                                    {
                                                                        kitchenRunups = BooleanOperationsUtils.ExecuteBooleanOperation(kitchenRunups, solid, BooleanOperationsType.Union);
                                                                    }
                                                                }

                                                                foreach (Solid solid in sideFacesAsSolid)
                                                                {
                                                                    if (kitchenRunups == null)
                                                                    {
                                                                        kitchenRunups = solid;
                                                                    }
                                                                    else if (kitchenRunups != null)
                                                                    {
                                                                        kitchenRunups = BooleanOperationsUtils.ExecuteBooleanOperation(kitchenRunups, solid, BooleanOperationsType.Union);
                                                                    }
                                                                }

                                                                //vii). Eliminate solid areas from wallFaceExtrus below vanity splashes
                                                                wallFaceExtrus = BooleanOperationsUtils.ExecuteBooleanOperation(wallFaceExtrus, kitchenRunups, BooleanOperationsType.Difference);
                                                            }

                                                        }

                                                    }

                                                    #endregion

                                                    #region b) subtract areas behind upper cabinets //for casework I'm getting phase error! when querying x.room so ignore this step for caseworks
                                                    List<FamilyInstance> caseworks = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().Where(x => x.Category.Id == new ElementId(BuiltInCategory.OST_Casework)).ToList();

                                                    if (caseworks.Any())
                                                    {
                                                        foreach (FamilyInstance casework in caseworks)
                                                        {

                                                            //Leave fillers for further analysis
                                                            if (casework.Symbol.Family.Name.ToLower().Contains("filler"))
                                                            {
                                                                continue;
                                                            }

                                                            Room caseworkRoom = casework.Room;//returning null some cases
                                                            if (caseworkRoom != null)
                                                            {
                                                                if (caseworkRoom.Name.ToLower().Contains("kitchen")) //somehow the room filter is working here [throw an exception to check out casework is place inside a Room!!]
                                                                {
                                                                    //caseworksolid null

                                                                    //failed to perform boolan operation for 2 solids
                                                                    Solid caseworkSolid = null;
                                                                    try {
                                                                        caseworkSolid = tailorBird.getAcumSolidFromElement(casework);
                                                                    }
                                                                    //just break this individual loop and let calculate wall finish indicating there is missing cabinets calculations
                                                                    catch (Exception ex) {
                                                                        
                                                                        //stb.AppendLine("Caseworks do not allow to estimate Wall Finish between:" + room.Name + " &  Wall Id: " + wall.Id.ToString());
                                                                        missingCaseworks.Add(new Tuple<string, string>(room.Name, casework.Id.ToString()));
                                                                        

                                                                        throwWarning = true;
                                                                        areas.Add(wallFace.Area);
                                                                        continue;
                                                                    }


                                                                    XYZ WallNormalDir = planarWallFc.FaceNormal;

                                                                    //filter faces parallel and against to wall face normal, also the one with greatest area
                                                                    List<Tuple<PlanarFace, double>> facesWithAreas = new List<Tuple<PlanarFace, double>>();
                                                                    if (caseworkSolid != null)
                                                                    {
                                                                        foreach (Face face in caseworkSolid.Faces)
                                                                        {
                                                                            PlanarFace planarface = face as PlanarFace;
                                                                            if (planarface.FaceNormal.IsAlmostEqualTo(-WallNormalDir))
                                                                            {
                                                                                facesWithAreas.Add(new Tuple<PlanarFace, double>(planarface, planarface.Area));
                                                                            }
                                                                        }


                                                                        //There might happen caseworks are not always in an xy axis so the facesWithAreas list will be empty, so skip them
                                                                        if (facesWithAreas.Any() == false)
                                                                        {
                                                                            continue;
                                                                        }

                                                                        //Pick the face with greatest area
                                                                        facesWithAreas = facesWithAreas.OrderByDescending(x => x.Item2).ToList();
                                                                        PlanarFace largestFace = facesWithAreas[0].Item1;

                                                                        //Extrude face in both directions
                                                                        Solid positiveExtru = GeometryCreationUtilities.CreateExtrusionGeometry(largestFace.GetEdgesAsCurveLoops(), WallNormalDir, widthExtrusion * 3);//original value 0.3
                                                                        Solid negativeExtru = GeometryCreationUtilities.CreateExtrusionGeometry(largestFace.GetEdgesAsCurveLoops(), -WallNormalDir, widthExtrusion * 3);
                                                                        //Join solids
                                                                        caseworkSolid = BooleanOperationsUtils.ExecuteBooleanOperation(positiveExtru, negativeExtru, BooleanOperationsType.Union);
                                                                        //subtract solid geometries
                                                                        wallFaceExtrus = BooleanOperationsUtils.ExecuteBooleanOperation(wallFaceExtrus, caseworkSolid, BooleanOperationsType.Difference);
                                                                    }
                                                                }
                                                            }






                                                        }
                                                    }

                                                    #endregion

                                                    List<Solid> solidBetween2fillers = new List<Solid>();
                                                    #region c) areas behind fillers
                                                    if (caseworks.Any())
                                                    {
                                                        foreach (FamilyInstance casework in caseworks)
                                                        {
                                                            if (casework.Symbol.Family.Name.ToLower().Contains("filler"))
                                                            {

                                                                Room caseworkRoom = casework.Room;

                                                                if (caseworkRoom.Name.ToLower().Contains("kitchen"))
                                                                {

                                                                    Solid fillerSolid = tailorBird.getAcumSolidFromElement(casework);
                                                                    XYZ WallNormalDir = planarWallFc.FaceNormal;

                                                                    //filter faces parallel and against to wall face normal, also the one with greatest area
                                                                    List<Tuple<PlanarFace, double>> facesWithAreas = new List<Tuple<PlanarFace, double>>();
                                                                    if (fillerSolid != null)
                                                                    {
                                                                        foreach (Face face in fillerSolid.Faces)
                                                                        {
                                                                            PlanarFace planarface = face as PlanarFace;
                                                                            if (planarface.FaceNormal.IsAlmostEqualTo(-WallNormalDir))
                                                                            {
                                                                                facesWithAreas.Add(new Tuple<PlanarFace, double>(planarface, planarface.Area));
                                                                            }
                                                                        }

                                                                        //There might happen caseworks are not always in an xy axis so the facesWithAreas list will be empty, so skip them
                                                                        if (facesWithAreas.Any() == false)
                                                                        {
                                                                            continue;
                                                                        }

                                                                        //Pick the face with greatest area
                                                                        facesWithAreas = facesWithAreas.OrderByDescending(x => x.Item2).ToList();
                                                                        PlanarFace largestFace = facesWithAreas[0].Item1;

                                                                        //Extrude face in it's direction
                                                                        Solid positiveExtru = GeometryCreationUtilities.CreateExtrusionGeometry(largestFace.GetEdgesAsCurveLoops(), largestFace.FaceNormal, widthExtrusion * 25);//2.5 original value

                                                                        //keep that solid in a list for further analysis
                                                                        solidBetween2fillers.Add(positiveExtru);

                                                                        //subtract solid geometries
                                                                        wallFaceExtrus = BooleanOperationsUtils.ExecuteBooleanOperation(wallFaceExtrus, positiveExtru, BooleanOperationsType.Difference);
                                                                    }
                                                                }


                                                            }
                                                        }
                                                    }


                                                    #endregion

                                                    #region d). areas between 2 near fillers
                                                    //extrude the faces that are not parallel to wall face
                                                    List<Solid> extrudedFillers = new List<Solid>();
                                                    foreach (Solid solid in solidBetween2fillers)
                                                    {

                                                        foreach (Face face in solid.Faces)
                                                        {
                                                            PlanarFace planarface = face as PlanarFace;
                                                            if (planarface != null)
                                                            {
                                                                if (planarface.FaceNormal.Z != 1 && planarface.FaceNormal.Z != -1)
                                                                {
                                                                    if (!planarface.FaceNormal.IsAlmostEqualTo(planarWallFc.FaceNormal) && !planarface.FaceNormal.IsAlmostEqualTo(-planarWallFc.FaceNormal))
                                                                    {
                                                                        Solid positiveExtr = GeometryCreationUtilities.CreateExtrusionGeometry(planarface.GetEdgesAsCurveLoops(), planarface.FaceNormal, widthExtrusion * 25); //2.5 original value
                                                                        Solid negativeExtr = GeometryCreationUtilities.CreateExtrusionGeometry(planarface.GetEdgesAsCurveLoops(), -planarface.FaceNormal, widthExtrusion * 25);
                                                                        Solid wholeExtr = BooleanOperationsUtils.ExecuteBooleanOperation(positiveExtr, negativeExtr, BooleanOperationsType.Union);
                                                                        extrudedFillers.Add(wholeExtr);
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }

                                                    //intersec solids with any other same extrusion. Use that to subtract
                                                    List<Solid> intersectionFillers = new List<Solid>();
                                                    for (int i = 0; i < extrudedFillers.Count; i++)
                                                    {
                                                        Solid item = extrudedFillers[i];

                                                        for (int j = 0; j < extrudedFillers.Count; j++)
                                                        {
                                                            if (i != j)
                                                            {
                                                                Solid intersection = BooleanOperationsUtils.ExecuteBooleanOperation(extrudedFillers[j], item, BooleanOperationsType.Intersect);
                                                                if (intersection.Volume != 0)
                                                                {
                                                                    intersectionFillers.Add(intersection);
                                                                }
                                                            }
                                                        }
                                                    }

                                                    //subtract intersection solids from the wallFaceExtrus
                                                    foreach (Solid intersecSolid in intersectionFillers)
                                                    {
                                                        wallFaceExtrus = BooleanOperationsUtils.ExecuteBooleanOperation(wallFaceExtrus, intersecSolid, BooleanOperationsType.Difference);
                                                    }

                                                    #endregion
                                                }

                                                #endregion

                                                //DirectShape ds = DirectShape.CreateElement(doc,new ElementId(BuiltInCategory.OST_GenericModel));
                                                //ds.SetShape(new List<GeometryObject>{wallFaceExtrus as GeometryObject});

                                                #region B.vi) Tessellate all the faces that heads toward the wallface

                                                foreach (Face face in wallFaceExtrus.Faces)
                                                {
                                                    PlanarFace planarFC = face as PlanarFace;
                                                    if (planarFC != null)
                                                    {
                                                        if (planarFC.FaceNormal.IsAlmostEqualTo(-planarWallFc.FaceNormal))
                                                        {
                                                            //tesselate face
                                                            if (materialWallFinish != null)
                                                            {
                                                                if (showWallFinishandBaseboard == true)
                                                                {
                                                                    try
                                                                    {
                                                                        tailorBird.tessellateFace(face, doc, materialWallFinish);
                                                                    }
                                                                    catch {
                                                                        failedToTessellate.Add(new Tuple<string, string>(room.Name, wall.Id.ToString()));
                                                                        throwWarning = true;
                                                                        areas.Add(wallFace.Area);
                                                                        //this time is ok to not draw the tessellate face but to take the area data
                                                                        //continue;
                                                                    }
                                                                    
                                                                }
                                                            }


                                                            //calculate area and accumulate the whole areas
                                                            roomWallFinishArea = roomWallFinishArea + face.Area;
                                                        }
                                                    }
                                                }



                                                #endregion

                                                #endregion

                                                //Important filter!! to not repeat faces!
                                                areas.Add(wallFace.Area);
                                            }
                                        }
                                    }
                                }
                            }
                        }


                        #region B.vii). set the value for wall Finish Area for each room
                        RvParam wallFinishArea = room.LookupParameter("Wall Finish Area");
                        if (wallFinishArea != null)
                        {
                            wallFinishArea.Set(roomWallFinishArea);
                        }


                        //reset the accumulation of the wallFinishArea
                        roomWallFinishArea = 0;
                        #endregion

                        roomsAnalyzed.Add(room.Id);
                    }
                }
            }
            //            if (deleteInstances.Any()) {
            //            	doc.Delete(deleteInstances);
            //            }
            #endregion

            #region 1.4) turn the wall joins back to bitter type
            if (allRoomsInModel.Any())
            {
                foreach (SpatialElement element in allRoomsInModel)
                {
                    SpatialElementGeometryCalculator cal2 = new SpatialElementGeometryCalculator(doc, sebOptions);
                    Room room = element as Room;
                    if (room != null)
                    {
                        //                    	if (!room.Name.Contains(roomTestName)) {
                        //                    		continue;
                        //                    	}

                        //Get Room Level
                        Level level = room.Level;
                        if (analyzeLevels == true)
                        {
                            RvParam lvlPar = level.get_Parameter(BuiltInParameter.DATUM_TEXT);
                            if (lvlPar != null)
                            {
                                if (!lvlPar.AsString().Contains(levelInput))
                                {
                                    continue;
                                }
                            }
                        }

                        #region Change WallJunionType
                        SpatialElementGeometryResults results2 = cal2.CalculateSpatialElementGeometry(room);
                        Solid roomSolid2 = results2.GetGeometry();
                        foreach (Face face in roomSolid2.Faces)
                        {
                            IList<SpatialElementBoundarySubface> subFaceList = results2.GetBoundaryFaceInfo(face);
                            foreach (SpatialElementBoundarySubface subface in subFaceList)
                            {
                                if (subface.SubfaceType == SubfaceType.Side)
                                {
                                    Wall wall = doc.GetElement(subface.SpatialBoundaryElement.HostElementId) as Wall;
                                    if (wall != null)
                                    {
                                        LocationCurve locCurve = wall.Location as LocationCurve;
                                        if (locCurve != null)
                                        {
                                            locCurve.set_JoinType(0, JoinType.Abut);
                                            locCurve.set_JoinType(1, JoinType.Abut);
                                        }
                                    }
                                }
                            }

                        }
                        doc.Regenerate();
                        #endregion
                    }
                }
            }
            #endregion

            #endregion
            tr.Commit();

            if (throwWarning == true)
            {
                if (missingBaseboard.Any()) {
                    missingBaseboard = missingBaseboard.Distinct().ToList();
                    stb.AppendLine("Missing Baseboard between Room && Wall: ");
                    foreach (Tuple<string,string> tuple in missingBaseboard ) {
                        stb.AppendLine(tuple.Item1 + " < > " + tuple.Item2);
                    }
                    stb.AppendLine("\n");
                }
                if (missingWallFinish.Any())
                {
                    missingWallFinish = missingWallFinish.Distinct().ToList();

                    stb.AppendLine("Missing Wall finish calculation between Room && Wall: ");
                    foreach (Tuple<string, string> tuple in missingWallFinish)
                    {
                        stb.AppendLine(tuple.Item1 + " < > " + tuple.Item2);
                    }
                    stb.AppendLine("\n");
                }
                if (failedToTessellate.Any()) {
                    failedToTessellate = failedToTessellate.Distinct().ToList();

                    stb.AppendLine("Failed to tessellate wall face between Room && Wall: ");
                    foreach (Tuple<string, string> tuple in failedToTessellate)
                    {
                        stb.AppendLine(tuple.Item1 + " < > " + tuple.Item2);
                    }
                    stb.AppendLine("\n");
                }

                if (missingCaseworks.Any())
                {
                    missingCaseworks = missingCaseworks.Distinct().ToList();

                    stb.AppendLine("Missing caseworks calculations for Wall Finish between Room && Wall: ");
                    foreach (Tuple<string, string> tuple in missingCaseworks)
                    {
                        stb.AppendLine(tuple.Item1 + " < > " + tuple.Item2);
                    }
                }

                TaskDialog.Show("Warning", stb.ToString());

            }



            return Result.Succeeded;
        }

    }
}

namespace utilities
{
    class tailorBirdUtilities
    {
        public void tessellateFace(Face wallFace, Document doc, Material materialWallFinish)
        {
            Mesh mesh = wallFace.Triangulate();
            int n = mesh.NumTriangles;
            List<XYZ> args = new List<XYZ>(3);

            TessellatedShapeBuilder builder = new TessellatedShapeBuilder();
            builder.OpenConnectedFaceSet(true);

            for (int i = 0; i < n; i++)
            {
                MeshTriangle triangle = mesh.get_Triangle(i);
                XYZ p1 = triangle.get_Vertex(0);
                XYZ p2 = triangle.get_Vertex(1);
                XYZ p3 = triangle.get_Vertex(2);

                args.Clear();
                args.Add(p1);
                args.Add(p2);
                args.Add(p3);

                TessellatedFace tf = new TessellatedFace(args, materialWallFinish.Id);

                if (builder.DoesFaceHaveEnoughLoopsAndVertices(tf))
                {
                    builder.AddFace(tf);

                }
            }
            builder.CloseConnectedFaceSet();
            builder.Build();
            builder.Target = TessellatedShapeBuilderTarget.AnyGeometry;
            builder.Fallback = TessellatedShapeBuilderFallback.Mesh;
            TessellatedShapeBuilderResult result = builder.GetBuildResult();
            ElementId categoryId = new ElementId(BuiltInCategory.OST_GenericModel);
            DirectShape ds = DirectShape.CreateElement(doc, categoryId);
            
            try { 
                IList<GeometryObject> objts = result.GetGeometricalObjects();
                ds.SetShape(objts);
                ds.Name = "walls";
            }
            catch {
                //error
            }


        }

        public XYZ getCentroidFromElement(Element mirror)
        {
            XYZ centroid = XYZ.Zero;
            GeometryElement geo = mirror.get_Geometry(new Options());
            //I'm assuming if solid appears, geometry instance is fake!
            if (geo.Any(x => x is Solid)) //Watch out, x as Solid can return Null!! How do I assure solid has a volume then?
            {
                foreach (GeometryObject geomObj in geo)
                {
                    if (geomObj is Solid)
                    {
                        Solid solidGeo = geomObj as Solid;
                        if (null != solidGeo)
                        {
                            if (solidGeo.Volume > 0.01)
                            {
                                centroid = solidGeo.ComputeCentroid();
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (GeometryObject geomObj in geo)
                {
                    if (geomObj is GeometryInstance)
                    {
                        GeometryInstance instance = geomObj as GeometryInstance;
                        if (null != instance)
                        {
                            GeometryElement geoElement = instance.GetInstanceGeometry();
                            foreach (GeometryObject geomEl in geoElement)
                            {
                                if (geomEl is Solid)
                                {
                                    Solid solid = geomEl as Solid;
                                    if (solid.Volume > 0.01)
                                    {
                                        centroid = solid.ComputeCentroid();
                                    }
                                }
                            }

                        }
                    }
                }
            }
            return centroid;
        }

        public bool isElementInsideRoom(Room room, XYZ centroid)
        {
            BoundingBoxXYZ bbroom = room.get_BoundingBox(null);
            double tol = 0.15;
            if ((bbroom.Min.X - tol) <= centroid.X && (bbroom.Min.Y - tol) <= centroid.Y && (bbroom.Max.X + tol) >= centroid.X && (bbroom.Max.Y + tol) >= centroid.Y)
            {
                if ((bbroom.Min.Z <= centroid.Z) && (centroid.Z <= bbroom.Max.Z))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }


        }

        public Solid getSolidFromElement(Element famIn)
        {

            Solid solid = null;
            GeometryElement geo = famIn.get_Geometry(new Options());
            //I'm assuming if solid appears, geometry instance is fake!
            if (geo.Any(x => x is Solid)) //Watch out, x as Solid can return Null!! How do I assure solid has a volume then?
            {
                foreach (GeometryObject geomObj in geo)
                {
                    if (geomObj is Solid)
                    {
                        Solid solidGeo = geomObj as Solid;
                        if (null != solidGeo)
                        {
                            if (solidGeo.Volume > 0.01)
                            {
                                if (solid == null)
                                {

                                    solid = solidGeo;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (GeometryObject geomObj in geo)
                {
                    if (geomObj is GeometryInstance)
                    {
                        GeometryInstance instance = geomObj as GeometryInstance;
                        if (null != instance)
                        {
                            GeometryElement geoElement = instance.GetInstanceGeometry();
                            foreach (GeometryObject geomEl in geoElement)
                            {
                                if (geomEl is Solid)
                                {
                                    Solid solidGeo = geomEl as Solid;
                                    if (solidGeo != null)
                                    {
                                        if (solidGeo.Volume > 0.01)
                                        {
                                            if (solid == null)
                                            {
                                                solid = solidGeo;
                                            }
                                        }
                                    }
                                }
                            }

                        }
                    }
                }
            }
            return solid;
        }

        public Solid getAcumSolidFromElement(Element famIn)
        {

            Solid solid = null;
            GeometryElement geo = famIn.get_Geometry(new Options());
            //I'm assuming if solid appears, geometry instance is fake!
            if (geo.Any(x => x is Solid)) //Watch out, x as Solid can return Null!! How do I assure solid has a volume then?
            {
                foreach (GeometryObject geomObj in geo)
                {
                    if (geomObj is Solid)
                    {
                        Solid solidGeo = geomObj as Solid;
                        if (null != solidGeo)
                        {
                            if (solidGeo.Volume > 0.01)
                            {
                                if (solid == null)
                                {
                                    solid = solidGeo;
                                }
                                else
                                {
                                    solid = BooleanOperationsUtils.ExecuteBooleanOperation(solid, solidGeo, BooleanOperationsType.Union);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (GeometryObject geomObj in geo)
                {
                    if (geomObj is GeometryInstance)
                    {
                        GeometryInstance instance = geomObj as GeometryInstance;
                        if (null != instance)
                        {
                            GeometryElement geoElement = instance.GetInstanceGeometry();
                            foreach (GeometryObject geomEl in geoElement)
                            {
                                if (geomEl is Solid)
                                {
                                    Solid solidGeo = geomEl as Solid;
                                    if (solidGeo != null)
                                    {
                                        if (solidGeo.Volume > 0.01)
                                        {
                                            if (solid == null)
                                            {
                                                solid = solidGeo;
                                            }
                                            else
                                            {
                                                solid = BooleanOperationsUtils.ExecuteBooleanOperation(solid, solidGeo, BooleanOperationsType.Union);
                                            }
                                        }
                                    }
                                }
                            }

                        }
                    }
                }
            }
            return solid;
        }

        public CurveLoop getCurveLoopFromOpennings(Element famIn)
        {

            Solid solid = null;
            Options opts = new Options();
            opts.IncludeNonVisibleObjects = true;
            GeometryElement geo = famIn.get_Geometry(opts);
            //I'm assuming if solid appears, geometry instance is fake!
            if (geo.Any(x => x is Solid)) //Watch out, x as Solid can return Null!! How do I assure solid has a volume then?
            {
                foreach (GeometryObject geomObj in geo)
                {
                    if (geomObj is Solid)
                    {
                        Solid solidGeo = geomObj as Solid;
                        if (null != solidGeo)
                        {
                            if (solidGeo.Volume > 0.01)
                            {
                                if (solid == null)
                                {
                                    solid = solidGeo;
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (GeometryObject geomObj in geo)
                {
                    if (geomObj is GeometryInstance)
                    {
                        GeometryInstance instance = geomObj as GeometryInstance;
                        if (null != instance)
                        {
                            GeometryElement geoElement = instance.GetInstanceGeometry();
                            foreach (GeometryObject geomEl in geoElement)
                            {
                                if (geomEl is Solid)
                                {
                                    Solid solidGeo = geomEl as Solid;
                                    if (solidGeo != null)
                                    {
                                        if (solidGeo.Volume > 0.01)
                                        {
                                            if (solid == null)
                                            {
                                                solid = solidGeo;
                                            }
                                        }
                                    }
                                }
                            }

                        }
                    }
                }
            }

            List<Tuple<Face, double>> facesAreas = new List<Tuple<Face, double>>();
            foreach (Face fc in solid.Faces)
            {
                PlanarFace plFc = fc as PlanarFace;
                if (plFc != null)
                {
                    XYZ normal = plFc.FaceNormal;
                    if (normal.Z != 1 && normal.Z != -1)
                    {
                        facesAreas.Add(new Tuple<Face, double>(fc, plFc.Area));
                    }
                }
            }
            facesAreas = facesAreas.OrderByDescending(x => x.Item2).ToList();
            Face theFace = facesAreas[0].Item1;
            CurveLoop thecurveLoop = theFace.GetEdgesAsCurveLoops()[0];

            return thecurveLoop;
        }

        public Solid createSkirtingBoardSolidFromWall(PlanarFace planarface, Solid roomSolid2, double skirtingboardHeight, double skirtingboardWidth)
        {
            Solid solid = null;

            //there is one edgearray as there are no gaps! So there is no need to use a foreach but seems I cant index and EdgeArrayArray, It's neccessary to use a loop
            foreach (EdgeArray edgeArray in planarface.EdgeLoops)
            {

                //Get the bottom edge curve
                List<Tuple<Curve, double>> edgeElevation = new List<Tuple<Curve, double>>();
                foreach (Edge edge in edgeArray)
                {
                    Curve curve = edge.AsCurve();
                    double elevation = (curve.GetEndPoint(0).Z + curve.GetEndPoint(1).Z) / 2;

                    edgeElevation.Add(new Tuple<Curve, double>(curve, elevation));
                }
                edgeElevation = edgeElevation.OrderBy(x => x.Item2).ToList();


                Curve bottomCurve = edgeElevation[0].Item1;

                //Create the top offset skirtingboard edge curve
                XYZ start = new XYZ(bottomCurve.GetEndPoint(0).X, bottomCurve.GetEndPoint(0).Y, bottomCurve.GetEndPoint(0).Z + skirtingboardHeight);
                XYZ end = new XYZ(bottomCurve.GetEndPoint(1).X, bottomCurve.GetEndPoint(1).Y, bottomCurve.GetEndPoint(1).Z + skirtingboardHeight);

                Line topCurve = Line.CreateBound(start, end);

                //Create curveLoop
                IList<CurveLoop> skirtingBoardFromWall = new List<CurveLoop>();
                List<Curve> curveList = new List<Curve>();

                Line closingLine = Line.CreateBound(topCurve.GetEndPoint(0), bottomCurve.GetEndPoint(0));
                Line initConectLine = Line.CreateBound(bottomCurve.GetEndPoint(1), topCurve.GetEndPoint(1));
                Curve topCurveReversed = topCurve.CreateReversed();

                curveList.Add(bottomCurve);
                curveList.Add(initConectLine);
                curveList.Add(topCurveReversed);
                curveList.Add(closingLine);

                skirtingBoardFromWall.Add(CurveLoop.Create(curveList));

                //Create Net skirtingboard from wall
                Solid skirtingboardRealWidth = GeometryCreationUtilities.CreateExtrusionGeometry(skirtingBoardFromWall, planarface.FaceNormal, skirtingboardWidth);

                //make sure the skirting board it's surrounding the room
                try
                {
                    skirtingboardRealWidth = BooleanOperationsUtils.ExecuteBooleanOperation(roomSolid2, skirtingboardRealWidth, BooleanOperationsType.Intersect);
                    solid = skirtingboardRealWidth;

                }
                catch
                {


                }




            }

            return solid;
        }

    }
    class FamilyLoadSettings : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }
        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }

}