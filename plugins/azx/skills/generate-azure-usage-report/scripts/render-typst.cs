#:property PublishAot=false
#:package System.CommandLine@2.0.0-beta5

using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

// =========================================================================
// Stage 10 — render the compact usage report as a self-contained Typst
// document from usage-report.json (+ optional narrative.json), optionally
// compiling to PDF. Layout: cover → Resumen Ejecutivo (narrative + KPI row
// + riesgos) → the 6 numbered sections (data blocks + per-section Análisis
// callouts) → closing. All emitted UI strings are Spanish.
// =========================================================================

var stageDirOption = new Option<DirectoryInfo>("--stage-dir") { Required = true, Description = "Staging directory.", HelpName = "PATH" };
var narrativeOption = new Option<FileInfo?>("--narrative") { Description = "Narrative JSON path. Default: {stage-dir}/narrative.json.", HelpName = "PATH" };
var stylesOption = new Option<DirectoryInfo?>("--styles") { Description = "Style-guide directory (theme.json + logo). Default: {stage-dir}/.styles when present; absent = neutral, brand-free output.", HelpName = "DIR" };
var titleOption = new Option<string?>("--title") { Description = "Report title. Overrides the style guide.", HelpName = "TEXT" };
var companyOption = new Option<string?>("--company") { Description = "Client company name. Overrides the style guide.", HelpName = "NAME" };
var authorOption = new Option<string?>("--author") { Description = "Issuing company name (cover + closing). Overrides the style guide.", HelpName = "NAME" };
var logoOption = new Option<FileInfo?>("--logo") { Description = "Logo path (PNG). Overrides the style guide. Copied to typst/logo.png.", HelpName = "PATH" };
var primaryOption = new Option<string?>("--primary-color") { Description = "Primary color hex. Overrides the style guide.", HelpName = "HEX" };
var secondaryOption = new Option<string?>("--secondary-color") { Description = "Secondary color hex. Overrides the style guide.", HelpName = "HEX" };
var panelOption = new Option<string?>("--panel-color") { Description = "Light panel color hex. Overrides the style guide.", HelpName = "HEX" };
var fontOption = new Option<string?>("--font") { Description = "Body font family name. Overrides the style guide. Font files can ship in {styles}/fonts/ (copied to typst/fonts and passed via --font-path).", HelpName = "NAME" };
var compileOption = new Option<bool>("--compile") { Description = "Run `typst compile main.typ output.pdf` if available." };
var forceOption = new Option<bool>("--force") { Description = "Overwrite typst/." };

var rootCommand = new RootCommand("Stage 10 — render the light usage report to Typst (and optionally PDF).")
{
    stageDirOption, narrativeOption, stylesOption, titleOption, companyOption, authorOption, logoOption,
    primaryOption, secondaryOption, panelOption, fontOption, compileOption, forceOption,
};

rootCommand.SetAction(async (parseResult, ct) =>
{
    var stageDir = parseResult.GetValue(stageDirOption)!;
    var narrativeArg = parseResult.GetValue(narrativeOption);
    var compile = parseResult.GetValue(compileOption);
    var force = parseResult.GetValue(forceOption);

    // --- theme resolution: CLI flag > .styles/theme.json > neutral default ---
    // Without a style guide the report is deliberately brand-free: neutral gray
    // palette, no company, no author, no logo (azx whitelabel convention).
    var stylesDir = parseResult.GetValue(stylesOption)
        ?? new DirectoryInfo(Path.Combine(stageDir.FullName, ".styles"));
    var theme = await StyleGuide.Load(stylesDir, ct);

    var title = parseResult.GetValue(titleOption) ?? theme.Title ?? "Reporte de Uso de Azure";
    var company = parseResult.GetValue(companyOption) ?? theme.Company;
    var author = parseResult.GetValue(authorOption) ?? theme.Author;
    var primary = parseResult.GetValue(primaryOption) ?? theme.PrimaryColor ?? "#6B7280";
    var secondary = parseResult.GetValue(secondaryOption) ?? theme.SecondaryColor ?? "#374151";
    var panel = parseResult.GetValue(panelOption) ?? theme.PanelColor ?? "#F3F4F6";
    var font = parseResult.GetValue(fontOption) ?? theme.Font ?? "Segoe UI";
    var logo = parseResult.GetValue(logoOption) ?? theme.Logo;

    var reportPath = Path.Combine(stageDir.FullName, "usage-report.json");
    if (!File.Exists(reportPath))
    {
        await Console.Error.WriteLineAsync("[render-typst] usage-report.json missing. Run build-usage-report first.");
        return 2;
    }

    var typstDir = new DirectoryInfo(Path.Combine(stageDir.FullName, "typst"));
    if (typstDir.Exists && !force)
    {
        await Console.Error.WriteLineAsync($"[render-typst] {typstDir.FullName} exists. Use --force to overwrite.");
        return 1;
    }
    if (typstDir.Exists) typstDir.Delete(recursive: true);
    typstDir.Create();

    JsonElement report;
    using (var stream = File.OpenRead(reportPath))
    using (var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct))
        report = doc.RootElement.Clone();

    var narrativePath = narrativeArg?.FullName ?? Path.Combine(stageDir.FullName, "narrative.json");
    JsonElement? narrative = null;
    if (File.Exists(narrativePath))
    {
        using var stream = File.OpenRead(narrativePath);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        narrative = doc.RootElement.Clone();
        await Console.Error.WriteLineAsync($"[render-typst] loaded narrative {narrativePath}");
    }
    else
    {
        await Console.Error.WriteLineAsync($"[render-typst] no narrative at {narrativePath}; rendering data tables only (Análisis omitted).");
    }

    var model = new ReportModel(report, narrative, title, company, author, logo is not null);

    await File.WriteAllTextAsync(
        Path.Combine(typstDir.FullName, "data.json"),
        JsonSerializer.Serialize(model.BuildDataPayload(primary, secondary, panel), Json.Opts), ct);

    if (logo is not null && logo.Exists)
        File.Copy(logo.FullName, Path.Combine(typstDir.FullName, "logo.png"), overwrite: true);

    // Brand fonts ship inside the style guide ({styles}/fonts/*.otf|ttf) and are
    // copied into typst/fonts so the project stays self-contained; the compile
    // step points typst at them via --font-path.
    var hasFonts = false;
    if (theme.FontsDir is { Exists: true } fontsDir)
    {
        var target = Directory.CreateDirectory(Path.Combine(typstDir.FullName, "fonts"));
        foreach (var f in fontsDir.EnumerateFiles().Where(f =>
            f.Extension.ToLowerInvariant() is ".otf" or ".ttf" or ".ttc"))
        {
            File.Copy(f.FullName, Path.Combine(target.FullName, f.Name), overwrite: true);
            hasFonts = true;
        }
    }

    var preamble = Templates.Preamble(primary, secondary, panel, title, author, font);
    var body = TypstWriter.Render(model);
    await File.WriteAllTextAsync(Path.Combine(typstDir.FullName, "main.typ"), preamble + "\n\n" + body, Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(typstDir.FullName, "report.typ"), body, Encoding.UTF8);

    await Console.Error.WriteLineAsync($"[render-typst] wrote {typstDir.FullName}");

    if (compile)
    {
        var pdfPath = Path.Combine(stageDir.FullName, "output.pdf");
        var ok = await TryCompile(typstDir.FullName, pdfPath, hasFonts, ct);
        if (!ok)
        {
            var fontArg = hasFonts ? "--font-path fonts " : "";
            await Console.Error.WriteLineAsync("[render-typst] typst CLI not available or compile failed. To compile manually:");
            await Console.Error.WriteLineAsync($"[render-typst]   typst compile {fontArg}\"{Path.Combine(typstDir.FullName, "main.typ")}\" \"{pdfPath}\"");
            return 1;
        }
        await Console.Error.WriteLineAsync($"[render-typst] compiled {pdfPath}");
    }
    return 0;
});

return await rootCommand.Parse(args).InvokeAsync();

static async Task<bool> TryCompile(string typstDir, string outputPdf, bool hasFonts, CancellationToken ct)
{
    try
    {
        var fontArg = hasFonts ? "--font-path fonts " : "";
        var psi = new ProcessStartInfo("typst", $"compile {fontArg}\"main.typ\" \"{outputPdf}\"")
        {
            WorkingDirectory = typstDir,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi);
        if (proc is null) return false;
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync(ct);
            await Console.Error.WriteLineAsync($"[render-typst] typst stderr: {err}");
            return false;
        }
        return true;
    }
    catch { return false; }
}

// =========================================================================
// StyleGuide — optional style-guide directory (default {stage-dir}/.styles):
//   theme.json   { "title"?, "company"?, "author"?, "primaryColor"?,
//                  "secondaryColor"?, "panelColor"?, "font"?, "logo"? }
//   logo.png     picked up automatically when theme.json has no "logo" key
//   fonts/       brand font files (.otf/.ttf), copied to typst/fonts and
//                passed to `typst compile` via --font-path
// Known keys override the neutral defaults; unknown keys are reported to
// stderr and ignored. Absent directory = neutral, brand-free report.
// =========================================================================
sealed class StyleGuide
{
    public string? Title;
    public string? Company;
    public string? Author;
    public string? PrimaryColor;
    public string? SecondaryColor;
    public string? PanelColor;
    public string? Font;
    public FileInfo? Logo;
    public DirectoryInfo? FontsDir;

    static readonly string[] KnownKeys = { "title", "company", "author", "primaryColor", "secondaryColor", "panelColor", "font", "logo" };

    public static async Task<StyleGuide> Load(DirectoryInfo dir, CancellationToken ct)
    {
        var sg = new StyleGuide();
        if (!dir.Exists) return sg;
        await Console.Error.WriteLineAsync($"[render-typst] using style guide {dir.FullName}");

        string? logoRef = null;
        var themePath = Path.Combine(dir.FullName, "theme.json");
        if (File.Exists(themePath))
        {
            using var stream = File.OpenRead(themePath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
                switch (prop.Name)
                {
                    case "title": sg.Title = value; break;
                    case "company": sg.Company = value; break;
                    case "author": sg.Author = value; break;
                    case "primaryColor": sg.PrimaryColor = value; break;
                    case "secondaryColor": sg.SecondaryColor = value; break;
                    case "panelColor": sg.PanelColor = value; break;
                    case "font": sg.Font = value; break;
                    case "logo": logoRef = value; break;
                    default:
                        await Console.Error.WriteLineAsync($"[render-typst] theme.json: unknown key '{prop.Name}' ignored (known: {string.Join(", ", KnownKeys)}).");
                        break;
                }
            }
        }

        var logoPath = logoRef is not null
            ? Path.Combine(dir.FullName, logoRef)
            : Path.Combine(dir.FullName, "logo.png");
        if (File.Exists(logoPath)) sg.Logo = new FileInfo(logoPath);
        else if (logoRef is not null)
            await Console.Error.WriteLineAsync($"[render-typst] theme.json names logo '{logoRef}' but the file is missing; rendering without logo.");
        var fonts = new DirectoryInfo(Path.Combine(dir.FullName, "fonts"));
        if (fonts.Exists) sg.FontsDir = fonts;
        return sg;
    }
}

// =========================================================================
// Model.
// =========================================================================
sealed class ReportModel
{
    public string Title { get; }
    public string? Company { get; }
    public string? Author { get; }
    public string SubscriptionId { get; }
    public string GeneratedAt { get; }
    public IReadOnlyList<string> Months { get; }
    public string Currency { get; }
    public JsonElement Signals { get; }
    public JsonElement Sections { get; }
    public bool HasLogo { get; }
    public JsonElement? Narrative { get; }

    public ReportModel(JsonElement report, JsonElement? narrative, string title, string? company, string? author, bool hasLogo)
    {
        Title = title;
        Company = company;
        Author = author;
        SubscriptionId = Get(report, "subscriptionId") ?? "";
        GeneratedAt = DateTimeOffset.UtcNow.ToString("dd 'de' MMMM 'de' yyyy", new CultureInfo("es-ES"));
        Months = report.TryGetProperty("months", out var m) && m.ValueKind == JsonValueKind.Array
            ? m.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
            : new List<string>();
        Currency = Get(report, "currency") ?? "USD";
        Signals = report.TryGetProperty("signals", out var sg) ? sg : default;
        Sections = report.GetProperty("sections");
        HasLogo = hasLogo;
        Narrative = narrative;
    }

    static string? Get(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public object BuildDataPayload(string primary, string secondary, string panel) => new
    {
        meta = new { title = Title, companyName = Company, author = Author, subscriptionId = SubscriptionId, generatedAt = GeneratedAt, logoPath = HasLogo ? "logo.png" : null },
        branding = new { primaryColor = primary, secondaryColor = secondary, panelColor = panel },
        months = Months,
        currency = Currency,
        signals = Signals.ValueKind == JsonValueKind.Object ? (object)Signals : new { },
        narrative = Narrative.HasValue ? (object)Narrative.Value : new { },
        sections = Sections,
    };

    public JsonElement? ExecSummary =>
        Narrative is { } n && n.ValueKind == JsonValueKind.Object
        && n.TryGetProperty("executiveSummary", out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    // narrative.sections.<sectionId> => { resumen, observaciones, recomendaciones }
    public JsonElement? SectionAnalysis(string sectionId)
    {
        if (Narrative is { } n && n.ValueKind == JsonValueKind.Object
            && n.TryGetProperty("sections", out var s) && s.ValueKind == JsonValueKind.Object
            && s.TryGetProperty(sectionId, out var a) && a.ValueKind == JsonValueKind.Object)
            return a;
        return null;
    }
}

// =========================================================================
// TypstWriter.
// =========================================================================
static class TypstWriter
{
    public static string Render(ReportModel m)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// AUTO-GENERATED by render-typst.cs — do not edit by hand.");
        sb.AppendLine();
        RenderCover(sb, m);
        RenderExecutiveSummary(sb, m);
        RenderSections(sb, m);
        RenderClosing(sb, m);
        return sb.ToString();
    }

    // ---------------- Cover ----------------
    static void RenderCover(StringBuilder sb, ReportModel m)
    {
        sb.AppendLine("#v(2fr)");
        if (m.HasLogo)
            sb.AppendLine("#align(center)[#image(\"logo.png\", width: 180pt)]");
        sb.AppendLine("#v(1fr)");
        sb.AppendLine("#align(center)[");
        sb.AppendLine("  #block(width: 90%)[");
        sb.AppendLine("    #set text(size: 26pt, weight: \"bold\", fill: color-secondary)");
        sb.AppendLine($"    {Esc(m.Title)}");
        sb.AppendLine("  ]");
        sb.AppendLine("]");
        sb.AppendLine("#v(0.5fr)");
        sb.AppendLine("#align(center)[");
        sb.AppendLine("  #set text(size: 14pt, fill: color-secondary.lighten(20%))");
        sb.AppendLine($"  Período · {Esc(MonthRangeLong(m))}");
        sb.AppendLine("]");
        sb.AppendLine("#v(1fr)");
        sb.AppendLine("#align(center)[");
        sb.AppendLine("  #block(width: 72%, fill: color-panel, inset: 20pt, radius: 6pt)[");
        sb.AppendLine("    #set text(size: 10pt)");
        sb.AppendLine("    #grid(");
        sb.AppendLine("      columns: (auto, 1fr),");
        sb.AppendLine("      column-gutter: 12pt,");
        sb.AppendLine("      row-gutter: 10pt,");
        sb.AppendLine($"      [*Cliente:*],        [{Esc(m.Company ?? "—")}],");
        sb.AppendLine($"      [*Suscripción:*],    [{Raw(m.SubscriptionId)}],");
        sb.AppendLine($"      [*Período:*],        [{Esc(MonthRangeLong(m))}],");
        sb.AppendLine($"      [*Generado:*],       [{Esc(m.GeneratedAt)}],");
        if (!string.IsNullOrWhiteSpace(m.Author))
            sb.AppendLine($"      [*Elaborado por:*],  [{Esc(m.Author)}],");
        sb.AppendLine("    )");
        sb.AppendLine("  ]");
        sb.AppendLine("]");
        sb.AppendLine("#v(2fr)");
        sb.AppendLine("#align(center)[");
        sb.AppendLine("  #set text(size: 8pt, fill: color-secondary.lighten(40%))");
        sb.AppendLine("  Documento confidencial");
        sb.AppendLine("]");
        sb.AppendLine("#pagebreak()");
        sb.AppendLine();
    }

    // ---------------- Executive summary ----------------
    static void RenderExecutiveSummary(StringBuilder sb, ReportModel m)
    {
        var es = m.ExecSummary;
        sb.AppendLine("= Resumen Ejecutivo");
        sb.AppendLine();

        var estado = Str(es, "estadoGeneral");
        if (!string.IsNullOrWhiteSpace(estado))
        {
            sb.AppendLine(Esc(estado));
            sb.AppendLine();
            sb.AppendLine("#v(8pt)");
        }

        var atencion = Str(es, "atencion");
        if (!string.IsNullOrWhiteSpace(atencion))
        {
            Callout(sb, "naranja-alerta", "⚠", "Atención requerida", Esc(atencion));
            sb.AppendLine("#v(10pt)");
        }

        RenderKpiRow(sb, m);

        var hallazgos = Arr(es, "hallazgosClave");
        if (hallazgos.Count > 0)
        {
            sb.AppendLine("== Hallazgos Clave");
            sb.AppendLine();
            foreach (var h in hallazgos)
                sb.AppendLine($"- *{Esc(Str(h, "titulo"))}* — {Esc(Str(h, "texto"))}");
            sb.AppendLine();
            sb.AppendLine("#v(8pt)");
        }

        RenderRiesgosTable(sb, es);
        RenderProximosPasos(sb, es);

        sb.AppendLine("#pagebreak()");
        sb.AppendLine();
    }

    static void RenderKpiRow(StringBuilder sb, ReportModel m)
    {
        if (m.Signals.ValueKind != JsonValueKind.Object) return;
        var s = m.Signals;
        var currentCost = Num(s, "currentMonthCost");
        var mom = Num(s, "momGrowthPct");
        var curLabel = Str(s, "currentMonth") is { Length: >= 7 } cm ? MonthLong(cm) : "Mes actual";

        double? score = null;
        if (s.TryGetProperty("security", out var sec) && sec.ValueKind == JsonValueKind.Object
            && sec.TryGetProperty("secureScorePct", out var sp) && sp.ValueKind == JsonValueKind.Number)
            score = sp.GetDouble();
        long resources = 0;
        if (s.TryGetProperty("resourceCounts", out var rc) && rc.ValueKind == JsonValueKind.Object
            && rc.TryGetProperty("total", out var rt) && rt.ValueKind == JsonValueKind.Number)
            resources = rt.GetInt64();

        var momStr = (mom >= 0 ? "+" : "") + mom.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        var scoreStr = score is null ? "—" : score.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        sb.AppendLine("#grid(");
        sb.AppendLine("  columns: (1fr, 1fr, 1fr, 1fr),");
        sb.AppendLine("  column-gutter: 12pt,");
        sb.AppendLine($"  kpi(\"Costo del mes\", \"{MoneyLiteral(currentCost)}\", \"{StrLit(curLabel)}\"),");
        sb.AppendLine($"  kpi(\"Variación mensual\", \"{StrLit(momStr)}\", \"vs mes anterior\"),");
        sb.AppendLine($"  kpi(\"Secure Score\", \"{StrLit(scoreStr)}\", \"Defender for Cloud\"),");
        sb.AppendLine($"  kpi(\"Recursos\", \"{resources.ToString(CultureInfo.InvariantCulture)}\", \"En la suscripción\"),");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("#v(10pt)");
    }

    static void RenderRiesgosTable(StringBuilder sb, JsonElement? es)
    {
        var riesgos = Arr(es, "riesgos");
        if (riesgos.Count == 0) return;
        sb.AppendLine("== Matriz de Riesgos");
        sb.AppendLine();
        sb.AppendLine("#table(");
        sb.AppendLine("  columns: (1fr, 1.4fr, auto, 1.4fr),");
        sb.AppendLine("  fill: (_, y) => if y == 0 { color-secondary } else if calc.odd(y) { color-panel } else { blanco },");
        sb.AppendLine("  table.header(");
        sb.AppendLine("    [#set text(fill: blanco, weight: \"bold\"); Riesgo],");
        sb.AppendLine("    [#set text(fill: blanco, weight: \"bold\"); Impacto Potencial],");
        sb.AppendLine("    [#set text(fill: blanco, weight: \"bold\"); Probabilidad],");
        sb.AppendLine("    [#set text(fill: blanco, weight: \"bold\"); Acción Recomendada],");
        sb.AppendLine("  ),");
        foreach (var r in riesgos)
        {
            var prob = Str(r, "probabilidad");
            sb.AppendLine($"  [{Esc(Str(r, "riesgo"))}],");
            sb.AppendLine($"  [{Esc(Str(r, "impacto"))}],");
            sb.AppendLine($"  [#set text(fill: {StatusColor(prob) ?? "color-secondary"}, weight: \"bold\"); {Esc(prob)}],");
            sb.AppendLine($"  [{Esc(Str(r, "accion"))}],");
        }
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("#v(10pt)");
    }

    static void RenderProximosPasos(StringBuilder sb, JsonElement? es)
    {
        if (es is not { } e || !e.TryGetProperty("proximosPasos", out var pp) || pp.ValueKind != JsonValueKind.Object)
            return;
        var groups = new (string heading, List<string> items)[]
        {
            ("Inmediato", ArrStrings(pp, "inmediato")),
            ("Corto plazo", ArrStrings(pp, "cortoPlazo")),
            ("Mediano plazo", ArrStrings(pp, "medianoPlazo")),
        };
        if (groups.All(g => g.items.Count == 0)) return;
        sb.AppendLine("== Próximos Pasos");
        sb.AppendLine();
        foreach (var (heading, items) in groups)
        {
            if (items.Count == 0) continue;
            sb.AppendLine($"=== {heading}");
            sb.AppendLine();
            foreach (var i in items) sb.AppendLine($"- {Esc(i)}");
            sb.AppendLine();
        }
        sb.AppendLine("#v(8pt)");
    }

    // ---------------- Sections ----------------
    // Sections flow continuously (no per-section pagebreak) to keep the report compact.
    static void RenderSections(StringBuilder sb, ReportModel m)
    {
        var n = 0;
        foreach (var section in m.Sections.EnumerateArray())
        {
            n++;
            var sectionId = Str(section, "id");
            sb.AppendLine($"= {n}. {Esc(Str(section, "title"))}");
            sb.AppendLine();

            if (section.TryGetProperty("blocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in blocks.EnumerateArray())
                    RenderBlock(sb, m, block);
            }

            RenderAnalysis(sb, m.SectionAnalysis(sectionId));
            sb.AppendLine("#v(14pt)");
            sb.AppendLine();
        }
    }

    static void RenderBlock(StringBuilder sb, ReportModel m, JsonElement block)
    {
        var title = Str(block, "title");
        var displayType = Str(block, "displayType");
        var data = block.TryGetProperty("data", out var d) ? d : default;

        sb.AppendLine($"== {Esc(title)}");
        sb.AppendLine();

        // Every block opens with its `description` — a short Spanish line
        // explaining what the table/chart evaluates. Paragraph blocks already
        // render the description as their body, so they skip the intro.
        var intro = Str(block, "description");
        if (displayType != "Paragraph" && !string.IsNullOrWhiteSpace(intro))
        {
            sb.AppendLine($"#text(size: 9pt, style: \"italic\", fill: color-secondary.lighten(15%))[{Esc(intro)}]");
            sb.AppendLine("#v(2pt)");
            sb.AppendLine();
        }

        switch (displayType)
        {
            case "MonthlyTrend": RenderMonthlyTrend(sb, m, data); break;
            case "GroupedMonthly": RenderGroupedMonthly(sb, m, data); break;
            case "CountTable": RenderCountTable(sb, data); break;
            case "Table": RenderTable(sb, data); break;
            case "BarList": RenderBarList(sb, data); break;
            case "Paragraph": RenderParagraph(sb, block); break;
            default:
                sb.AppendLine($"#text(style: \"italic\")[Tipo de visualización no soportado: {Esc(displayType)}.]");
                sb.AppendLine();
                break;
        }
    }

    static void RenderAnalysis(StringBuilder sb, JsonElement? analysis)
    {
        if (analysis is not { } a) return;
        var resumen = Str(a, "resumen");
        var observaciones = ArrStrings(a, "observaciones");
        var recomendaciones = ArrStrings(a, "recomendaciones");
        if (string.IsNullOrWhiteSpace(resumen) && observaciones.Count == 0 && recomendaciones.Count == 0)
            return;

        sb.AppendLine("#v(6pt)");
        if (!string.IsNullOrWhiteSpace(resumen))
        {
            Callout(sb, "azul-info", "ℹ", "Resumen", Esc(resumen));
            sb.AppendLine("#v(6pt)");
        }
        if (observaciones.Count > 0)
        {
            Callout(sb, "naranja-alerta", "⚠", "Observaciones Clave", BulletBody(observaciones));
            sb.AppendLine("#v(6pt)");
        }
        if (recomendaciones.Count > 0)
        {
            Callout(sb, "verde-ok", "✔", "Recomendaciones", BulletBody(recomendaciones));
            sb.AppendLine("#v(6pt)");
        }
        sb.AppendLine();
    }

    // --- MonthlyTrend → column chart (value + bar + month + delta per column).
    //     The current (last) month renders in full primary color; prior months lightened. ---
    static void RenderMonthlyTrend(StringBuilder sb, ReportModel m, JsonElement data)
    {
        var totals = data.TryGetProperty("totals", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().ToList() : new List<JsonElement>();
        if (totals.Count == 0) { Empty(sb); return; }

        var max = Math.Max(totals.Max(r => Num(r, "value")), 1e-9);
        sb.AppendLine("#block(width: 100%, fill: color-panel.lighten(40%), inset: 12pt, radius: 5pt)[");
        sb.Append("#grid(\n  columns: (");
        sb.Append(string.Join(", ", Enumerable.Repeat("1fr", totals.Count)));
        sb.AppendLine("),");
        sb.AppendLine("  column-gutter: 14pt,");
        sb.AppendLine("  align: center + bottom,");
        for (var i = 0; i < totals.Count; i++)
        {
            var row = totals[i];
            var value = Num(row, "value");
            var height = Math.Max(3, value / max * 54).ToString("0.0", CultureInfo.InvariantCulture);
            var isCurrent = i == totals.Count - 1;
            var fill = isCurrent ? "color-primary" : "color-primary.lighten(45%)";
            var deltaLine = i == 0 ? "#text(size: 7.5pt, fill: color-secondary.lighten(30%))[—]"
                : $"#text(size: 7.5pt)[{DeltaArrow(Num(row, "delta"), Str(row, "direction"))}]";
            sb.AppendLine("  [");
            sb.AppendLine($"    #text(size: 9pt, weight: \"bold\")[{Money(value)}]");
            sb.AppendLine("    #v(3pt)");
            sb.AppendLine($"    #box(width: 68%, height: {height}pt, fill: {fill}, radius: 3pt)");
            sb.AppendLine("    #v(3pt)");
            sb.AppendLine($"    #text(size: 8pt, weight: {(isCurrent ? "\"bold\"" : "\"regular\"")})[{Esc(MonthShort(Str(row, "month")))}]");
            sb.AppendLine("    #linebreak()");
            sb.AppendLine($"    {deltaLine}");
            sb.AppendLine("  ],");
        }
        sb.AppendLine(")");
        sb.AppendLine("]");
        sb.AppendLine();
    }

    // --- BarList → label | proportional bar | value rows (chart replacing a table).
    //     Row colors: rojo | naranja | verde | azul → semáforo; absent → primary. ---
    static void RenderBarList(StringBuilder sb, JsonElement data)
    {
        var rows = data.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().ToList() : new List<JsonElement>();
        if (rows.Count == 0) { Empty(sb); return; }
        var money = Str(data, "format") == "money";

        var max = Math.Max(rows.Max(row => Num(row, "value")), 1e-9);
        sb.AppendLine("#grid(");
        sb.AppendLine("  columns: (5.2cm, 1fr, auto),");
        sb.AppendLine("  column-gutter: 10pt,");
        sb.AppendLine("  row-gutter: 7pt,");
        sb.AppendLine("  align: (left + horizon, left + horizon, right + horizon),");
        foreach (var row in rows)
        {
            var value = Num(row, "value");
            var pct = Math.Max(value / max * 100, value > 0 ? 1.5 : 0).ToString("0.0", CultureInfo.InvariantCulture);
            var fill = Str(row, "color") switch
            {
                "rojo" => "rojo-alerta",
                "naranja" => "naranja-alerta",
                "verde" => "verde-ok",
                "azul" => "azul-info",
                _ => "color-primary",
            };
            var valueText = money ? Money(value) : ((long)Math.Round(value)).ToString("#,0", CultureInfo.InvariantCulture);
            sb.AppendLine($"  [#text(size: 9pt)[{CellEsc(Str(row, "label"))}]],");
            sb.AppendLine(value > 0
                ? $"  [#box(width: {pct}%, height: 9pt, fill: {fill}.lighten(15%), radius: 2pt)],"
                : "  [#box(width: 0.5pt, height: 9pt)],");
            sb.AppendLine($"  [#text(size: 9pt, weight: \"bold\")[{valueText}]],");
        }
        sb.AppendLine(")");
        sb.AppendLine();
    }

    // --- GroupedMonthly ---
    static void RenderGroupedMonthly(StringBuilder sb, ReportModel m, JsonElement data)
    {
        var months = data.TryGetProperty("months", out var mm) && mm.ValueKind == JsonValueKind.Array
            ? mm.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
            : m.Months.ToList();
        var rows = data.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().ToList() : new List<JsonElement>();
        if (rows.Count == 0) { Empty(sb); return; }

        sb.Append("#table(\n  columns: (1fr");
        foreach (var _ in months) sb.Append(", auto");
        sb.AppendLine("),");
        sb.AppendLine("  fill: (_, y) => if y == 0 { color-secondary } else if calc.odd(y) { color-panel } else { blanco },");
        sb.AppendLine("  table.header(");
        sb.AppendLine("    [#set text(fill: blanco, weight: \"bold\"); Concepto],");
        foreach (var mo in months)
            sb.AppendLine($"    [#set text(fill: blanco, weight: \"bold\"); {Esc(MonthShort(mo))}],");
        sb.AppendLine("  ),");
        foreach (var row in rows)
        {
            var values = NumArray(row, "values");
            sb.AppendLine($"  [{CellEsc(Str(row, "label"))}],");
            foreach (var v in values) sb.AppendLine($"  [{Money(v)}],");
            for (var i = values.Count; i < months.Count; i++) sb.AppendLine("  [—],");
        }
        if (data.TryGetProperty("total", out var tot) && tot.ValueKind == JsonValueKind.Array)
        {
            var totals = tot.EnumerateArray().Select(x => x.GetDouble()).ToList();
            sb.AppendLine("  [*Total*],");
            foreach (var v in totals) sb.AppendLine($"  [*{Money(v)}*],");
            for (var i = totals.Count; i < months.Count; i++) sb.AppendLine("  [—],");
        }
        sb.AppendLine(")");
        sb.AppendLine();
    }

    // --- CountTable ---
    static void RenderCountTable(StringBuilder sb, JsonElement data)
    {
        var rows = data.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().ToList() : new List<JsonElement>();
        if (rows.Count == 0) { Empty(sb); return; }

        sb.AppendLine("#table(");
        sb.AppendLine("  columns: (1fr, auto),");
        sb.AppendLine("  fill: (_, y) => if y == 0 { color-secondary } else if calc.odd(y) { color-panel } else { blanco },");
        sb.AppendLine("  table.header(");
        sb.AppendLine("    [#set text(fill: blanco, weight: \"bold\"); Concepto],");
        sb.AppendLine("    [#set text(fill: blanco, weight: \"bold\"); Cantidad],");
        sb.AppendLine("  ),");
        foreach (var row in rows)
        {
            sb.AppendLine($"  [{CellEsc(Str(row, "label"))}],");
            sb.AppendLine($"  [{((long)Math.Round(Num(row, "count"))).ToString(CultureInfo.InvariantCulture)}],");
        }
        if (data.TryGetProperty("total", out var tot) && tot.ValueKind == JsonValueKind.Number)
        {
            sb.AppendLine("  [*Total*],");
            sb.AppendLine($"  [*{tot.GetInt64().ToString(CultureInfo.InvariantCulture)}*],");
        }
        sb.AppendLine(")");
        sb.AppendLine();
    }

    // --- Generic Table: { headers[], align[], rows[][], dense? } ---
    // Status tokens (Alta/Crítico/Zombie/OK/…) are colored via StatusColor.
    static void RenderTable(StringBuilder sb, JsonElement data)
    {
        var headers = data.TryGetProperty("headers", out var h) && h.ValueKind == JsonValueKind.Array
            ? h.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : new List<string>();
        var rows = data.TryGetProperty("rows", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().ToList() : new List<JsonElement>();
        if (headers.Count == 0 || rows.Count == 0) { Empty(sb); return; }

        var align = data.TryGetProperty("align", out var al) && al.ValueKind == JsonValueKind.Array
            ? al.EnumerateArray().Select(x => x.GetString() ?? "l").ToList() : new List<string>();
        var dense = (data.TryGetProperty("dense", out var dn) && dn.ValueKind == JsonValueKind.True)
            || rows.Count > 15 || headers.Count > 5;

        // Dense tables trade padding for content width and allow hyphenation so
        // medium-length words still fit the narrow text columns.
        if (dense) sb.AppendLine("#set text(size: 8pt, hyphenate: true)\n#set table(inset: 5pt)");
        // Text-heavy ("l") columns get fractional widths so long content wraps inside its
        // own cell instead of overflowing into the neighbour; numeric/centered columns
        // stay auto-sized. The first column is the row label and gets extra share.
        string ColSpec(int i)
        {
            var a = align.Count == headers.Count ? align[i] : "l";
            return a == "l" ? (i == 0 ? "1.6fr" : "1fr") : "auto";
        }
        var cols = string.Join(", ", headers.Select((_, i) => ColSpec(i)));
        sb.AppendLine($"#table(\n  columns: ({cols}),");
        if (align.Count == headers.Count)
        {
            var alignExpr = string.Join(", ", align.Select(a => a switch { "r" => "right", "c" => "center", _ => "left" }));
            sb.AppendLine($"  align: (x, y) => if y == 0 {{ left }} else {{ ({alignExpr}).at(x) }},");
        }
        sb.AppendLine("  fill: (_, y) => if y == 0 { color-secondary } else if calc.odd(y) { color-panel } else { blanco },");
        sb.AppendLine("  table.header(");
        foreach (var head in headers)
            sb.AppendLine($"    [#set text(fill: blanco, weight: \"bold\"); {Esc(head)}],");
        sb.AppendLine("  ),");
        foreach (var row in rows)
        {
            if (row.ValueKind != JsonValueKind.Array) continue;
            var cells = row.EnumerateArray().ToList();
            for (var i = 0; i < headers.Count; i++)
            {
                var text = i < cells.Count ? CellText(cells[i]) : "—";
                var color = StatusColor(text);
                sb.AppendLine(color is null
                    ? $"  [{CellEsc(text)}],"
                    : $"  [#set text(fill: {color}, weight: \"bold\"); {CellEsc(text)}],");
            }
        }
        sb.AppendLine(")");
        if (dense) sb.AppendLine("#set text(size: 10pt, hyphenate: false)\n#set table(inset: 8pt)");
        sb.AppendLine();
    }

    static string CellText(JsonElement cell) => cell.ValueKind switch
    {
        JsonValueKind.String => cell.GetString() ?? "—",
        JsonValueKind.Number => cell.TryGetInt64(out var l)
            ? l.ToString("#,0", CultureInfo.InvariantCulture)
            : cell.GetDouble().ToString("#,0.00", CultureInfo.InvariantCulture),
        JsonValueKind.True => "Sí",
        JsonValueKind.False => "No",
        JsonValueKind.Null => "—",
        _ => cell.ToString(),
    };

    static void RenderParagraph(StringBuilder sb, JsonElement block)
    {
        var desc = Str(block, "description");
        if (!string.IsNullOrWhiteSpace(desc)) { sb.AppendLine(Esc(desc)); sb.AppendLine(); }
        else Empty(sb);
    }

    static void RenderClosing(StringBuilder sb, ReportModel m)
    {
        sb.AppendLine("#align(center)[");
        sb.AppendLine("  #block(width: 80%, fill: color-panel, inset: 16pt, radius: 6pt)[");
        sb.AppendLine("    #set text(size: 9pt)");
        sb.AppendLine("    *Datos fuente:* `Microsoft.CostManagement` · `Microsoft.Insights` metrics · Log Analytics · `Microsoft.Advisor` · `Microsoft.Security`\\");
        sb.AppendLine($"    Suscripción: {Raw(m.SubscriptionId)} · Período {Esc(MonthRangeShort(m))}\\");
        sb.AppendLine("    #v(4pt)");
        var byWhom = string.IsNullOrWhiteSpace(m.Author) ? "" : $" por *{Esc(m.Author)}*";
        var forWhom = string.IsNullOrWhiteSpace(m.Company) ? "" : $" para *{Esc(m.Company)}*";
        sb.AppendLine($"    Documento generado el *{Esc(m.GeneratedAt)}*{byWhom}{forWhom}");
        sb.AppendLine("  ]");
        sb.AppendLine("]");
        sb.AppendLine();
    }

    // ---------------- helpers ----------------
    static void Callout(StringBuilder sb, string color, string icon, string title, string body, bool breakable = true)
    {
        var breakableArg = breakable ? "" : ", breakable: false";
        sb.AppendLine($"#callout({color}, \"{icon}\", \"{title}\"{breakableArg})[");
        sb.AppendLine($"  {body}");
        sb.AppendLine("]");
    }

    static string BulletBody(IReadOnlyList<string> items)
    {
        var sb = new StringBuilder();
        foreach (var i in items) sb.Append($"- {Esc(i)}\n  ");
        return sb.ToString().TrimEnd();
    }

    static void Empty(StringBuilder sb)
    {
        sb.AppendLine("#text(style: \"italic\", fill: color-secondary.lighten(30%))[Sin hallazgos en el período.]");
        sb.AppendLine();
    }

    static string DeltaArrow(double delta, string dir)
    {
        var (color, arrow) = dir switch
        {
            "up" => ("rojo-alerta", "⬆"),
            "down" => ("verde-ok", "⬇"),
            _ => ((string?)null, "–"),
        };
        var sign = delta > 0 ? "+" : delta < 0 ? "-" : "";
        var body = $"{sign}{Money(delta)} {arrow}";
        return color is null ? body : $"#set text(fill: {color}, weight: \"bold\"); {body}";
    }

    // Exact-match status tokens → semáforo color; null = plain cell.
    static string? StatusColor(string text) => text.Trim() switch
    {
        "Alta" or "High" or "Crítico" or "Vencido" or "Zombie" => "rojo-alerta",
        "Media" or "Medium" or "Advertencia" or "Sobredimensionado" or "Saturado" or "Detection" => "naranja-alerta",
        "Baja" or "Low" or "OK" or "Correcto" or "Enabled" or "Prevention" => "verde-ok",
        "Informativo" => "azul-info",
        _ => null,
    };

    static readonly string[] MonthNamesEs =
    {
        "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
        "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre",
    };
    static readonly string[] MonthAbbrEs =
    {
        "Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic",
    };

    static (int year, int month)? ParseMonthKey(string key)
    {
        if (key.Length >= 7 && int.TryParse(key.AsSpan(0, 4), out var y) && int.TryParse(key.AsSpan(5, 2), out var mo) && mo is >= 1 and <= 12)
            return (y, mo);
        return null;
    }

    static string MonthLong(string key) =>
        ParseMonthKey(key) is { } p ? $"{MonthNamesEs[p.month - 1]} {p.year}" : key;

    static string MonthShort(string key) =>
        ParseMonthKey(key) is { } p ? $"{MonthAbbrEs[p.month - 1]}-{p.year % 100:00}" : key;

    static string MonthRangeLong(ReportModel m)
    {
        if (m.Months.Count == 0) return "—";
        var first = MonthLong(m.Months[0]);
        var last = MonthLong(m.Months[^1]);
        return m.Months.Count == 1 ? first : $"{first} – {last}";
    }

    static string MonthRangeShort(ReportModel m)
    {
        if (m.Months.Count == 0) return "—";
        var first = MonthShort(m.Months[0]);
        var last = MonthShort(m.Months[^1]);
        return m.Months.Count == 1 ? first : $"{first} – {last}";
    }

    static string Money(double v) => "\\$" + Math.Abs(v).ToString("#,0.00", CultureInfo.InvariantCulture);
    static string MoneyLiteral(double v) => "$" + Math.Abs(v).ToString("#,0.00", CultureInfo.InvariantCulture);
    static string StrLit(string? s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    static string Str(JsonElement? el, string prop)
    {
        if (el is { } e && e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString() ?? "";
        return "";
    }
    static string Str(JsonElement el, string prop) => Str((JsonElement?)el, prop);

    static double Num(JsonElement el, string prop) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    static List<double> NumArray(JsonElement el, string prop)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array)
            return v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetDouble()).ToList();
        return new List<double>();
    }

    static List<JsonElement> Arr(JsonElement? el, string prop)
    {
        if (el is { } e && e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array)
            return v.EnumerateArray().ToList();
        return new List<JsonElement>();
    }

    static List<string> ArrStrings(JsonElement? el, string prop)
    {
        if (el is { } e && e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array)
            return v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? "").ToList();
        return new List<string>();
    }
    static List<string> ArrStrings(JsonElement el, string prop) => ArrStrings((JsonElement?)el, prop);

    // Table-cell escape: soft-break long unbroken tokens (ARM names, URIs,
    // thumbprint-style ids) so they wrap inside their own cell instead of
    // overflowing into the neighbour, then escape for Typst.
    static string CellEsc(string? s) => Esc(SoftBreak(s ?? ""));

    // Insert zero-width-space break opportunities after separators, at lower→Upper
    // camelCase boundaries, and every 10 chars inside unbroken alphanumeric runs.
    static string SoftBreak(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 16);
        var run = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (i > 0 && char.IsUpper(ch) && char.IsLower(s[i - 1]) && run >= 4) { sb.Append('​'); run = 0; }
            sb.Append(ch);
            if (ch is '/' or '-' or '_' or '.' or ':' or '\\') { sb.Append('​'); run = 0; }
            else if (char.IsWhiteSpace(ch)) run = 0;
            else if (++run >= 10) { sb.Append('​'); run = 0; }
        }
        return sb.ToString();
    }

    // Escape ALL Typst markup-significant chars in interpolated text.
    static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '*': sb.Append("\\*"); break;
                case '#': sb.Append("\\#"); break;
                case '$': sb.Append("\\$"); break;
                case '@': sb.Append("\\@"); break;
                case '<': sb.Append("\\<"); break;
                case '>': sb.Append("\\>"); break;
                case '_': sb.Append("\\_"); break;
                case '`': sb.Append("\\`"); break;
                case '[': sb.Append("\\["); break;
                case ']': sb.Append("\\]"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    static string Raw(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "—";
        return $"`{s.Replace("`", "")}`";
    }
}

// =========================================================================
// Templates — theme preamble (colors parametrized; layout ported from the
// Codster reference reports used by the WAF pipeline).
// =========================================================================
static class Templates
{
    public static string Preamble(string primary, string secondary, string panel, string title, string? author, string font)
    {
        var headerTitle = HeaderText(title);
        var authorLit = TypstStr(author ?? "");
        return $$"""
        // =========================================================================
        // Light usage report theme — generated by render-typst.cs
        // =========================================================================

        #let color-primary   = rgb("{{primary}}")
        #let color-secondary = rgb("{{secondary}}")
        #let color-panel     = rgb("{{panel}}")
        #let blanco          = rgb("#FFFFFF")

        // — Paleta semáforo —
        #let rojo-alerta    = rgb("#C62828")
        #let naranja-alerta = rgb("#E65100")
        #let verde-ok       = rgb("#33691E")
        #let azul-info      = rgb("#1565C0")

        #set document(title: {{TypstStr(title)}}, author: {{authorLit}})

        #set text(font: "{{font}}", size: 10pt, fill: color-secondary, hyphenate: false)

        #set page(
          paper: "a4",
          margin: (top: 2.5cm, bottom: 2cm, left: 2cm, right: 2cm),
          header: context {
            if counter(page).get().first() > 1 [
              #set text(size: 8pt, fill: color-secondary.lighten(30%))
              #grid(
                columns: (1fr, 1fr),
                align(left)[{{headerTitle}}],
                align(right)[#counter(page).display()],
              )
              #v(-4pt)
              #line(length: 100%, stroke: 0.5pt + color-secondary.lighten(60%))
            ]
          },
          footer: context {
            if counter(page).get().first() == 1 [
              #align(center)[
                #set text(size: 8pt, fill: color-secondary.lighten(30%))
                #counter(page).display() / #counter(page).final().first()
              ]
            ]
          },
        )

        #set heading(numbering: none)
        #show heading.where(level: 1): it => {
          v(8pt)
          block(width: 100%)[
            #set text(size: 16pt, weight: "bold", fill: color-secondary)
            #it.body
            #v(-2pt)
            #line(length: 100%, stroke: 2pt + color-primary)
          ]
          v(4pt)
        }
        #show heading.where(level: 2): it => {
          v(6pt)
          block[
            #set text(size: 13pt, weight: "bold", fill: color-secondary)
            #it.body
          ]
          v(2pt)
        }
        #show heading.where(level: 3): it => {
          v(4pt)
          block[
            #set text(size: 11pt, weight: "bold", fill: color-primary.darken(15%))
            #it.body
          ]
          v(2pt)
        }

        #set table(stroke: 0.5pt + color-secondary.lighten(60%), inset: 8pt)

        #show raw.where(block: false): it => {
          box(fill: color-secondary.lighten(92%), inset: (x: 4pt, y: 2pt), radius: 2pt)[
            #set text(font: "Consolas", size: 9pt, fill: color-secondary)
            #it
          ]
        }

        #let callout(color, icon, title, body, breakable: true) = {
          block(width: 100%, fill: color.lighten(80%), stroke: 1.5pt + color, radius: 5pt, inset: 12pt, breakable: breakable)[
            #grid(
              columns: (auto, 1fr),
              column-gutter: 8pt,
              align(top)[#set text(size: 13pt); #icon],
              align(top)[
                #set text(weight: "bold", fill: color.darken(10%))
                #title \
                #set text(weight: "regular", fill: color-secondary)
                #body
              ]
            )
          ]
        }

        #let kpi(label, value, sub) = {
          block(fill: color-panel, inset: 12pt, radius: 5pt, width: 100%)[
            #set text(size: 8pt, fill: color-secondary.lighten(20%))
            #label
            #v(2pt)
            #set text(size: 18pt, weight: "bold", fill: color-secondary)
            #value
            #v(1pt)
            #set text(size: 8pt, fill: color-secondary.lighten(30%))
            #sub
          ]
        }

        #set par(justify: true, leading: 0.65em)
        """;
    }

    static string TypstStr(string s)
    {
        var escaped = (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"\"{escaped}\"";
    }

    static string HeaderText(string s)
    {
        if (string.IsNullOrEmpty(s)) return "Reporte";
        var sb = new StringBuilder(s.Length + 8);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '#': sb.Append("\\#"); break;
                case '$': sb.Append("\\$"); break;
                case '_': sb.Append("\\_"); break;
                case '[': sb.Append("\\["); break;
                case ']': sb.Append("\\]"); break;
                case '*': sb.Append("\\*"); break;
                case '@': sb.Append("\\@"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }
}

static class Json
{
    public static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
