#!/usr/bin/env dotnet
#:property PublishAot=false
// json-to-vsdx — deterministic JSON diagram spec -> native Visio .vsdx (MS-VSDX, Visio 2013+).
// Pure BCL (System.IO.Compression + System.Xml.Linq); no external packages, no Visio required.
// Contract: json-to-vsdx.md. Units are inches, origin bottom-left (Visio native).

using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

XNamespace V = "http://schemas.microsoft.com/office/visio/2012/main";
XNamespace RD = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
XNamespace CT = "http://schemas.openxmlformats.org/package/2006/content-types";
XNamespace PR = "http://schemas.openxmlformats.org/package/2006/relationships";

string? input = null, output = null;
for (int i = 0; i < args.Length; i++)
    switch (args[i])
    {
        case "--input" when i + 1 < args.Length: input = args[++i]; break;
        case "--output" when i + 1 < args.Length: output = args[++i]; break;
        default: return Fail(1, $"unknown arg '{args[i]}' — usage: json-to-vsdx.cs --input spec.json --output diagram.vsdx");
    }
if (input is null || output is null) return Fail(1, "usage: json-to-vsdx.cs --input spec.json --output diagram.vsdx");
if (!File.Exists(input)) return Fail(1, $"input not found: {input}");

Spec spec;
try
{
    spec = JsonSerializer.Deserialize<Spec>(File.ReadAllText(input),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
        ?? throw new JsonException("spec is null");
}
catch (JsonException ex) { return Fail(2, $"invalid JSON spec: {ex.Message}"); }

// ---- validate ----
var errors = new List<string>();
var nodes = spec.Nodes ?? new();
var edges = spec.Edges ?? new();
var groups = spec.Groups ?? new();
if (nodes.Count == 0) errors.Add("spec has no nodes");
var dup = nodes.GroupBy(n => n.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
if (dup.Count > 0) errors.Add($"duplicate node ids: {string.Join(", ", dup)}");
var byId = nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First());
foreach (var e in edges)
    if (!byId.ContainsKey(e.From) || !byId.ContainsKey(e.To)) errors.Add($"edge {e.From}->{e.To} references unknown node");
foreach (var g in groups)
    foreach (var m in g.Members ?? new())
        if (!byId.ContainsKey(m)) errors.Add($"group '{g.Label}' references unknown node '{m}'");
foreach (var n in nodes)
    if (n.Shape is not (null or "rectangle" or "rounded" or "ellipse")) errors.Add($"node '{n.Id}': unknown shape '{n.Shape}' (rectangle|rounded|ellipse)");
if (errors.Count > 0) return Fail(2, string.Join("\n", errors));

// ---- layout (auto when any node lacks x/y): layered left-to-right by longest path from sources ----
const double DefW = 1.6, DefH = 1.0, HGap = 1.1, VGap = 0.55, Margin = 0.75;
var pos = new Dictionary<string, (double X, double Y, double W, double H)>();
if (nodes.Any(n => n.X is null || n.Y is null))
{
    var layer = nodes.ToDictionary(n => n.Id, _ => 0);
    for (int pass = 0; pass < nodes.Count; pass++)                       // Bellman-Ford style; cycle-safe by pass cap
        foreach (var e in edges)
            if (layer[e.To] < layer[e.From] + 1 && layer[e.From] + 1 < nodes.Count) layer[e.To] = layer[e.From] + 1;
    var cols = nodes.GroupBy(n => layer[n.Id]).OrderBy(g => g.Key).ToList();
    double contentH = cols.Max(c => c.Sum(n => n.H ?? DefH) + (c.Count() - 1) * VGap);
    foreach (var col in cols)
    {
        double x = Margin + col.Key * (DefW + HGap) + (col.Max(n => n.W ?? DefW)) / 2;
        double colH = col.Sum(n => n.H ?? DefH) + (col.Count() - 1) * VGap;
        double y = Margin + contentH - (contentH - colH) / 2;            // vertically centered, stacked top-down
        foreach (var n in col)
        {
            double h = n.H ?? DefH;
            pos[n.Id] = (x, y - h / 2, n.W ?? DefW, h);
            y -= h + VGap;
        }
    }
}
else
    foreach (var n in nodes) pos[n.Id] = (n.X!.Value, n.Y!.Value, n.W ?? DefW, n.H ?? DefH);

double pageW = spec.PageWidth ?? Math.Ceiling(pos.Values.Max(p => p.X + p.W / 2) + Margin);
double pageH = spec.PageHeight ?? Math.Ceiling(pos.Values.Max(p => p.Y + p.H / 2) + Margin);

// ---- build shapes ----
string N(double d) => d.ToString("0.####", CultureInfo.InvariantCulture);
XElement Cell(string n, string v, string? f = null) =>
    new(V + "Cell", new XAttribute("N", n), new XAttribute("V", v), f is null ? null : new XAttribute("F", f));
XElement Row(string t, int ix, double x, double y) =>
    new(V + "Row", new XAttribute("T", t), new XAttribute("IX", ix), Cell("X", N(x)), Cell("Y", N(y)));
XElement CharSize(double pt) =>
    new(V + "Section", new XAttribute("N", "Character"), new XElement(V + "Row", new XAttribute("IX", 0), Cell("Size", N(pt / 72.0))));
XElement RectGeom() =>
    new(V + "Section", new XAttribute("N", "Geometry"), new XAttribute("IX", 0), Cell("NoFill", "0"), Cell("NoLine", "0"),
        Row("RelMoveTo", 1, 0, 0), Row("RelLineTo", 2, 1, 0), Row("RelLineTo", 3, 1, 1), Row("RelLineTo", 4, 0, 1), Row("RelLineTo", 5, 0, 0));
XElement EllipseGeom(double w, double h) =>
    new(V + "Section", new XAttribute("N", "Geometry"), new XAttribute("IX", 0), Cell("NoFill", "0"), Cell("NoLine", "0"),
        new XElement(V + "Row", new XAttribute("T", "Ellipse"), new XAttribute("IX", 1),
            Cell("X", N(w / 2), "Width*0.5"), Cell("Y", N(h / 2), "Height*0.5"),
            Cell("A", N(w), "Width*1"), Cell("B", N(h / 2), "Height*0.5"),
            Cell("C", N(w / 2), "Width*0.5"), Cell("D", N(h), "Height*1")));
XElement Box(int id, double x, double y, double w, double h, string? label, string? fill, string? line,
             bool dashed, bool rounded, bool ellipse, double fontPt, bool topAlign) =>
    new(V + "Shape", new XAttribute("ID", id), new XAttribute("NameU", $"Sheet.{id}"), new XAttribute("Type", "Shape"),
        new XAttribute("LineStyle", "0"), new XAttribute("FillStyle", "0"), new XAttribute("TextStyle", "0"),
        Cell("PinX", N(x)), Cell("PinY", N(y)), Cell("Width", N(w)), Cell("Height", N(h)),
        Cell("LocPinX", N(w / 2), "Width*0.5"), Cell("LocPinY", N(h / 2), "Height*0.5"), Cell("Angle", "0"),
        Cell("FillPattern", fill is null ? "0" : "1"), fill is null ? null : Cell("FillForegnd", fill),
        Cell("LinePattern", dashed ? "2" : "1"), line is null ? null : Cell("LineColor", line),
        rounded ? Cell("Rounding", "0.12") : null, topAlign ? Cell("VerticalAlign", "0") : null,
        CharSize(fontPt), ellipse ? EllipseGeom(w, h) : RectGeom(),
        string.IsNullOrEmpty(label) ? null : new XElement(V + "Text", label));

var shapeId = new Dictionary<string, int>();
int nextId = 1;
var shapes = new List<XElement>();
var connects = new List<XElement>();

foreach (var g in groups)                                               // groups first = behind (z-order is document order)
{
    var members = (g.Members ?? new()).Select(m => pos[m]).ToList();
    if (members.Count == 0) continue;
    double inset = 0.3, x0 = members.Min(p => p.X - p.W / 2) - inset, x1 = members.Max(p => p.X + p.W / 2) + inset;
    double y0 = members.Min(p => p.Y - p.H / 2) - inset, y1 = members.Max(p => p.Y + p.H / 2) + inset + (g.Label is null ? 0 : 0.25);
    shapes.Add(Box(nextId++, (x0 + x1) / 2, (y0 + y1) / 2, x1 - x0, y1 - y0, g.Label, null, g.Line ?? "#7f7f7f", true, false, false, 9, true));
}
foreach (var n in nodes)
{
    var p = pos[n.Id];
    shapeId[n.Id] = nextId;
    shapes.Add(Box(nextId++, p.X, p.Y, p.W, p.H, n.Label ?? n.Id, n.Fill ?? "#ffffff", n.Line ?? "#404040",
        false, n.Shape == "rounded", n.Shape == "ellipse", n.FontSize ?? 10, false));
}
foreach (var e in edges)
{
    var a = pos[e.From]; var b = pos[e.To];
    // clip each endpoint at the node's bounding box so the static line meets the border, not the center
    (double X, double Y) Clip((double X, double Y, double W, double H) s, (double X, double Y, double W, double H) t)
    {
        double dx = t.X - s.X, dy = t.Y - s.Y;
        double k = Math.Min(dx == 0 ? double.MaxValue : Math.Abs(s.W / 2 / dx), dy == 0 ? double.MaxValue : Math.Abs(s.H / 2 / dy));
        return (s.X + dx * k, s.Y + dy * k);
    }
    var (bx, by) = Clip(a, b); var (ex, ey) = Clip(b, a);
    double len = Math.Max(Math.Sqrt((ex - bx) * (ex - bx) + (ey - by) * (ey - by)), 0.01);
    int id = nextId++;
    shapes.Add(new XElement(V + "Shape", new XAttribute("ID", id), new XAttribute("NameU", $"Sheet.{id}"), new XAttribute("Type", "Shape"),
        new XAttribute("LineStyle", "0"), new XAttribute("FillStyle", "0"), new XAttribute("TextStyle", "0"),
        Cell("BeginX", N(bx), $"_WALKGLUE(BegTrigger,EndTrigger,WalkPreference)"),
        Cell("BeginY", N(by), $"_WALKGLUE(BegTrigger,EndTrigger,WalkPreference)"),
        Cell("EndX", N(ex), $"_WALKGLUE(EndTrigger,BegTrigger,WalkPreference)"),
        Cell("EndY", N(ey), $"_WALKGLUE(EndTrigger,BegTrigger,WalkPreference)"),
        Cell("BegTrigger", "2", $"_XFTRIGGER(Sheet.{shapeId[e.From]}!EventXFMod)"),
        Cell("EndTrigger", "2", $"_XFTRIGGER(Sheet.{shapeId[e.To]}!EventXFMod)"),
        Cell("PinX", N((bx + ex) / 2), "(BeginX+EndX)/2"), Cell("PinY", N((by + ey) / 2), "(BeginY+EndY)/2"),
        Cell("Width", N(len), "SQRT((EndX-BeginX)^2+(EndY-BeginY)^2)"), Cell("Height", "0"),
        Cell("LocPinX", N(len / 2), "Width*0.5"), Cell("LocPinY", "0"),
        Cell("Angle", N(Math.Atan2(ey - by, ex - bx)), "ATAN2(EndY-BeginY,EndX-BeginX)"),
        Cell("GlueType", "2"), Cell("ObjType", "2"),
        Cell("LinePattern", e.Dashed == true ? "2" : "1"), Cell("LineColor", e.Line ?? "#404040"),
        Cell("BeginArrow", "0"), Cell("EndArrow", e.Arrow == false ? "0" : "4"), Cell("FillPattern", "0"),
        CharSize(e.FontSize ?? 8),
        new XElement(V + "Section", new XAttribute("N", "Geometry"), new XAttribute("IX", 0),
            Cell("NoFill", "1"), Cell("NoLine", "0"),
            Row("MoveTo", 1, 0, 0),
            new XElement(V + "Row", new XAttribute("T", "LineTo"), new XAttribute("IX", 2), Cell("X", N(len), "Width*1"), Cell("Y", "0"))),
        string.IsNullOrEmpty(e.Label) ? null : new XElement(V + "Text", e.Label)));
    connects.Add(new XElement(V + "Connect", new XAttribute("FromSheet", id), new XAttribute("FromCell", "BeginX"),
        new XAttribute("FromPart", 9), new XAttribute("ToSheet", shapeId[e.From]), new XAttribute("ToCell", "PinX"), new XAttribute("ToPart", 3)));
    connects.Add(new XElement(V + "Connect", new XAttribute("FromSheet", id), new XAttribute("FromCell", "EndX"),
        new XAttribute("FromPart", 12), new XAttribute("ToSheet", shapeId[e.To]), new XAttribute("ToCell", "PinX"), new XAttribute("ToPart", 3)));
}

// ---- assemble parts ----
XDocument Doc(XElement root) => new(new XDeclaration("1.0", "utf-8", "yes"), root);
var parts = new Dictionary<string, XDocument>
{
    ["[Content_Types].xml"] = Doc(new XElement(CT + "Types",
        new XElement(CT + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
        new XElement(CT + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
        new XElement(CT + "Override", new XAttribute("PartName", "/visio/document.xml"), new XAttribute("ContentType", "application/vnd.ms-visio.drawing.main+xml")),
        new XElement(CT + "Override", new XAttribute("PartName", "/visio/pages/pages.xml"), new XAttribute("ContentType", "application/vnd.ms-visio.pages+xml")),
        new XElement(CT + "Override", new XAttribute("PartName", "/visio/pages/page1.xml"), new XAttribute("ContentType", "application/vnd.ms-visio.page+xml")),
        new XElement(CT + "Override", new XAttribute("PartName", "/visio/windows.xml"), new XAttribute("ContentType", "application/vnd.ms-visio.windows+xml")))),
    ["_rels/.rels"] = Doc(new XElement(PR + "Relationships",
        new XElement(PR + "Relationship", new XAttribute("Id", "rId1"),
            new XAttribute("Type", "http://schemas.microsoft.com/visio/2010/relationships/document"), new XAttribute("Target", "visio/document.xml")))),
    ["visio/document.xml"] = Doc(new XElement(V + "VisioDocument", new XAttribute(XNamespace.Xmlns + "r", RD.NamespaceName),
        new XElement(V + "DocumentSettings", new XAttribute("TopPage", "0")),
        new XElement(V + "StyleSheets", new XElement(V + "StyleSheet", new XAttribute("ID", "0"), new XAttribute("NameU", "No Style"),
            Cell("EnableLineProps", "1"), Cell("EnableFillProps", "1"), Cell("EnableTextProps", "1"),
            Cell("LineWeight", "0.01"), Cell("LinePattern", "1"), Cell("LineColor", "#000000"),
            Cell("FillPattern", "1"), Cell("FillForegnd", "#ffffff"),
            new XElement(V + "Section", new XAttribute("N", "Character"), new XElement(V + "Row", new XAttribute("IX", 0),
                Cell("Font", "Calibri"), Cell("Size", "0.1389"), Cell("Color", "#000000"))),
            new XElement(V + "Section", new XAttribute("N", "Paragraph"), new XElement(V + "Row", new XAttribute("IX", 0), Cell("HorzAlign", "1"))))))),
    ["visio/_rels/document.xml.rels"] = Doc(new XElement(PR + "Relationships",
        new XElement(PR + "Relationship", new XAttribute("Id", "rId1"),
            new XAttribute("Type", "http://schemas.microsoft.com/visio/2010/relationships/pages"), new XAttribute("Target", "pages/pages.xml")),
        new XElement(PR + "Relationship", new XAttribute("Id", "rId2"),
            new XAttribute("Type", "http://schemas.microsoft.com/visio/2010/relationships/windows"), new XAttribute("Target", "windows.xml")))),
    // windows.xml is REQUIRED — Visio refuses the package without it ("missing or invalid parts")
    ["visio/windows.xml"] = Doc(new XElement(V + "Windows", new XAttribute("ClientWidth", "1280"), new XAttribute("ClientHeight", "800"),
        new XElement(V + "Window", new XAttribute("ID", "0"), new XAttribute("WindowType", "Drawing"),
            new XAttribute("ContainerType", "Page"), new XAttribute("Page", "0"), new XAttribute("ViewScale", "1"),
            new XAttribute("ViewCenterX", N(pageW / 2)), new XAttribute("ViewCenterY", N(pageH / 2))))),
    ["visio/pages/pages.xml"] = Doc(new XElement(V + "Pages", new XAttribute(XNamespace.Xmlns + "r", RD.NamespaceName),
        new XElement(V + "Page", new XAttribute("ID", "0"), new XAttribute("NameU", "Page-1"), new XAttribute("Name", spec.Title ?? "Page-1"),
            new XElement(V + "PageSheet", new XAttribute("LineStyle", "0"), new XAttribute("FillStyle", "0"), new XAttribute("TextStyle", "0"),
                Cell("PageWidth", N(pageW)), Cell("PageHeight", N(pageH)),
                Cell("PageScale", "1"), Cell("DrawingScale", "1")),
            new XElement(V + "Rel", new XAttribute(RD + "id", "rId1"))))),
    ["visio/pages/_rels/pages.xml.rels"] = Doc(new XElement(PR + "Relationships",
        new XElement(PR + "Relationship", new XAttribute("Id", "rId1"),
            new XAttribute("Type", "http://schemas.microsoft.com/visio/2010/relationships/page"), new XAttribute("Target", "page1.xml")))),
    ["visio/pages/page1.xml"] = Doc(new XElement(V + "PageContents", new XAttribute(XNamespace.Xmlns + "r", RD.NamespaceName),
        new XElement(V + "Shapes", shapes),
        connects.Count == 0 ? null : new XElement(V + "Connects", connects))),
};

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)) ?? ".");
if (File.Exists(output)) File.Delete(output);                            // ZipArchiveMode.Create requires a fresh file
using (var zip = ZipFile.Open(output, ZipArchiveMode.Create))
    foreach (var (path, doc) in parts)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero); // fixed: same input -> same bytes
        using var s = entry.Open();
        doc.Save(s);
    }

// ---- self-check: reopen and re-parse every XML part ----
using (var zip = ZipFile.OpenRead(output))
    foreach (var entry in zip.Entries)
    {
        using var s = entry.Open();
        try { XDocument.Load(s); } catch (Exception ex) { return Fail(1, $"self-check failed for {entry.FullName}: {ex.Message}"); }
    }

Console.WriteLine(JsonSerializer.Serialize(new
{
    output = Path.GetFullPath(output),
    nodes = nodes.Count,
    edges = edges.Count,
    groups = groups.Count,
    pageWidth = pageW,
    pageHeight = pageH,
}));
return 0;

int Fail(int code, string msg)
{
    Console.Error.WriteLine($"json-to-vsdx: {msg}");
    return code;
}

record Spec(string? Title, double? PageWidth, double? PageHeight, List<Node>? Nodes, List<Edge>? Edges, List<Group>? Groups);
record Node(string Id, string? Label, double? X, double? Y, double? W, double? H, string? Shape, string? Fill, string? Line, double? FontSize);
record Edge(string From, string To, string? Label, bool? Arrow, bool? Dashed, string? Line, double? FontSize);
record Group(string? Label, List<string>? Members, string? Line);
