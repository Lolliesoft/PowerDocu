using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerDocu.Common;
using Rubjerg.Graphviz;

namespace PowerDocu.SolutionDocumenter
{
    public class DataverseGraphBuilder
    {
        private readonly SolutionDocumentationContent content;
        private readonly Dictionary<string, string> entityColors = new Dictionary<string, string>();
        private List<TableEntity> tableEntities;
        private List<EntityRelationship> entityRelationships;
        private RootGraph rootGraph;
        public DataverseGraphBuilder(SolutionDocumentationContent contentToUse)
        {
            content = contentToUse;
            Directory.CreateDirectory(content.folderPath);
            buildGraph();
        }

        private void buildGraph()
        {
            rootGraph = RootGraph.CreateNew(
                GraphType.Undirected,
                CharsetHelper.GetSafeName(content.solution.UniqueName)
            );
            Graph.IntroduceAttribute(rootGraph, "compound", "true");
            Graph.IntroduceAttribute(rootGraph, "color", "#000090");
            Graph.IntroduceAttribute(rootGraph, "style", "filled");
            Graph.IntroduceAttribute(rootGraph, "fillcolor", "white");
            Graph.IntroduceAttribute(rootGraph, "label", " ");
            Graph.IntroduceAttribute(rootGraph, "rankdir", "LR");
            Graph.IntroduceAttribute(rootGraph, "fontname", "helvetica");
            Graph.IntroduceAttribute(rootGraph, "penwidth", "1");
            Node.IntroduceAttribute(rootGraph, "shape", "rectangle");
            Node.IntroduceAttribute(rootGraph, "color", "#000090");
            Node.IntroduceAttribute(rootGraph, "style", "filled");
            Node.IntroduceAttribute(rootGraph, "fillcolor", "white");
            Node.IntroduceAttribute(rootGraph, "label", "");
            Node.IntroduceAttribute(rootGraph, "fontname", "helvetica");
            Node.IntroduceAttribute(rootGraph, "fontcolor", "#ffffff");
            Node.IntroduceAttribute(rootGraph, "penwidth", "1");
            Edge.IntroduceAttribute(rootGraph, "color", "#000090");
            Edge.IntroduceAttribute(rootGraph, "penwidth", "1");

            tableEntities = content.solution.Customizations.getEntities();
            entityRelationships = content.solution.Customizations.getEntityRelationships();
            List<string> manyToManyEntityNames = entityRelationships
                .Where(o => o.getRelationshipType().Equals("ManyToMany"))
                .Select(o => o.getFirstEntityName())
                .ToList();

            // Create containers for all tables that are being looked up (1-many relationships)
            foreach (
                TableEntity tableEntity in tableEntities.Where(
                    o => o.containsNonDefaultLookupColumns() || manyToManyEntityNames.Contains(o.getName())
                )
            )
            {
                SubGraph currentTableGraph = CreateSubGraph(
                    "cluster_" + tableEntity.getName(),
                    tableEntity.getLocalizedName() + " (" + tableEntity.getName() + ")",
                    "#7070E0"
                );

                // Loop through all columns to find lookup columns (one-to-many relationships)
                foreach (
                    ColumnEntity lookupColumn in tableEntity
                        .GetColumns()
                        .Where(o => o.isNonDefaultLookUpColumn())
                        .ToList()
                )
                {
                    if (lookupColumn == null)
                    {
                        Console.WriteLine($"Warning: lookupColumn is null for table {tableEntity.getName()}.");
                        continue;
                    }

                    TableEntity lookupTableEntity = tableEntities.Find(
                        o => o.getName().ToLower().Equals(lookupColumn.getLogicalName())
                    );

                    SubGraph lookupTableGraph = null;
                    if (lookupTableEntity == null)
                    {
                        // Check if we have it in entityRelationships
                        EntityRelationship entityRelationship = entityRelationships
                            .FirstOrDefault(o => o.getReferencingAttributeName().ToLower() == lookupColumn.getLogicalName());

                        if (entityRelationship != null)
                        {
                            lookupTableEntity = tableEntities.Find(
                                o => o.getName().Equals(entityRelationship.getReferencedEntityName())
                            );

                            if (lookupTableEntity != null)
                            {
                                lookupTableGraph = rootGraph.GetOrAddSubgraph(
                                    CharsetHelper.GetSafeName("cluster_" + lookupTableEntity.getName())
                                );
                                lookupTableGraph.SetAttribute(
                                    "label",
                                    lookupTableEntity.getLocalizedName() + " (" + lookupTableEntity.getName() + ")"
                                );

                                createNodeRelationship(lookupTableGraph, currentTableGraph, lookupTableEntity, tableEntity, lookupColumn, "*|1");
                            }
                        }
                    }
                    else
                    {
                        lookupTableGraph = CreateSubGraph(
                            "cluster_" + lookupColumn.getLogicalName(),
                            lookupTableEntity.getLocalizedName() + " (" + lookupTableEntity.getName() + ")"
                        );

                        createNodeRelationship(lookupTableGraph, currentTableGraph, lookupTableEntity, tableEntity, lookupColumn, "*|1");
                    }
                }

                // Many-to-many relationships; relationship is between IDs of two tables
                if (manyToManyEntityNames.Contains(tableEntity.getName()))
                {
                    string second = entityRelationships
                        .FirstOrDefault(o => o.getFirstEntityName().Equals(tableEntity.getName()))
                        ?.getSecondEntityName();

                    if (second != null)
                    {
                        TableEntity secondTableEntity = tableEntities.Find(o => o.getName().Equals(second));
                        if (secondTableEntity != null)
                        {
                            ColumnEntity idColumn = tableEntity.getPrimaryColumnEntity();
                            if (idColumn == null)
                            {
                                Console.WriteLine($"Warning: idColumn is null for table {tableEntity.getName()}.");
                                continue;
                            }

                            SubGraph lookupTableGraph = rootGraph.GetOrAddSubgraph(
                                CharsetHelper.GetSafeName("cluster_" + secondTableEntity.getName())
                            );

                            if (lookupTableGraph != null)
                            {
                                lookupTableGraph.SetAttribute(
                                    "label",
                                    secondTableEntity.getLocalizedName() + " (" + secondTableEntity.getName() + ")"
                                );

                                createNodeRelationship(lookupTableGraph, currentTableGraph, secondTableEntity, tableEntity, idColumn, "*|*");
                            }
                        }
                    }
                }
            }

            rootGraph.ComputeLayout(LayoutEngines.Dot);
            generateImageFiles(rootGraph);
        }

        private void createNodeRelationship(SubGraph lookupTableGraph, SubGraph currentTableGraph, TableEntity lookupTableEntity, TableEntity tableEntity, ColumnEntity lookupColumn, string edgeLabel)
        {
            if (lookupTableEntity == null)
            {
                throw new ArgumentNullException(nameof(lookupTableEntity), "lookupTableEntity is null");
            }
            if (tableEntity == null)
            {
                throw new ArgumentNullException(nameof(tableEntity), "tableEntity is null");
            }
            if (lookupColumn == null)
            {
                throw new ArgumentNullException(nameof(lookupColumn), "lookupColumn is null");
            }

            string nodeEdgeColor = getHexColor(lookupColumn.getLogicalName());

            Node primaryColumnNode = CreateNode(
                lookupTableGraph,
                lookupTableEntity.getName() + "-" + lookupTableEntity.getPrimaryColumn(),
                lookupTableEntity.getPrimaryColumn() + " (Key)",
                nodeEdgeColor
            );

            Node lookupColumnNode = CreateNode(
                currentTableGraph,
                tableEntity.getName() + "-" + lookupColumn.getDisplayName(),
                lookupColumn.getDisplayName(),
                nodeEdgeColor
            );

            if (primaryColumnNode == null || lookupColumnNode == null)
            {
                throw new NullReferenceException("CreateNode returned null for one of the nodes");
            }

            _ = CreateEdge(
                lookupColumnNode,
                primaryColumnNode,
                "Lookup " + tableEntity.getLocalizedName() + " - " + lookupColumn.getDisplayName() + " - " + lookupColumn.getLogicalName(),
                nodeEdgeColor,
                "3",
                edgeLabel
            );
        }


        private SubGraph CreateSubGraph(string clusterName, string label, string color = null)
        {
            SubGraph subGraph = rootGraph.GetOrAddSubgraph(CharsetHelper.GetSafeName(clusterName));
            subGraph.SetAttribute("label", label);
            if (color != null)
            {
                subGraph.SetAttribute("color", color);
            }
            return subGraph;
        }

        private Node CreateNode(SubGraph subGraph, string nodeName, string label, string fillColor)
        {
            Node node = subGraph.GetOrAddNode(CharsetHelper.GetSafeName(nodeName));
            node.SetAttribute("label", CharsetHelper.GetSafeName(label));
            node.SetAttribute("fillcolor", fillColor);
            return node;
        }

        private Edge CreateEdge(Node fromNode, Node toNode, string name, string color, string penWidth, string label)
        {
            Edge edge = rootGraph.GetOrAddEdge(fromNode, toNode, name);
            edge.SetAttribute("color", color);
            edge.SetAttribute("penwidth", penWidth);
            edge.SetAttribute("label", label);
            return edge;
        }

        private string generateImageFiles(RootGraph rootGraph)
        {
            //Generate image files
            rootGraph.ToSvgFile(content.folderPath + "dataverse.svg");
            rootGraph.ToPngFile(content.folderPath + "dataverse.png");
            //the following code is no longer required, as saving directly to PNG is now possible through GraphViz. Keeping it in case it is required in the future
            /*
            var svgDocument = SvgDocument.Open(content.folderPath + "dataverse.svg");
            //generating the PNG from the SVG
            using (var bitmap = svgDocument.Draw())
            {
                bitmap?.Save(content.folderPath + "dataverse.png");
            }*/
            return "dataverse";
        }

        private string getHexColor(string lookupColumnName)
        {
            entityColors.TryGetValue(lookupColumnName, out string colour);
            if (String.IsNullOrEmpty(colour))
            {
                string[] colors = new string[]
                {
                    "#d35400",
                    "#008000",
                    "#3455DB",
                    "#9400d3",
                    "#939393",
                    "#b8806b",
                    "#D35400",
                    "#008b8b",
                    "#B50000",
                    "#1460aa",
                    "#8b008b",
                    "#696969",
                    "#634806",
                    "#870c25"
                };
                colour = colors[entityColors.Count % colors.Length];
                entityColors.Add(lookupColumnName, colour);
            }
            return colour;
        }
    }
}
