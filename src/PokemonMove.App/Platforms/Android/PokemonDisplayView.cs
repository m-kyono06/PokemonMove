using Android.Content;
using Android.Graphics;
using Android.Views;
using PokemonMove.Domain;
using AColor = Android.Graphics.Color;
using View = Android.Views.View;
using Paint = Android.Graphics.Paint;
using RectF = Android.Graphics.RectF;
using Canvas = Android.Graphics.Canvas;

namespace PokemonMove.App;

internal sealed class PokemonDisplayView(Context context) : View(context)
{
    private InspectionSnapshot state = new(null, null, DexVariant.All, "下画面でポケモンを選択してください。");
    private readonly Paint paint = new(PaintFlags.AntiAlias);
    private Bitmap? sprite;
    private string spriteKey = "";
    public void Update(InspectionSnapshot next)
    {
        state = next;
        var species = next.Pokemon?.Species ?? next.Species?.Species ?? 0;
        var shiny = next.Pokemon?.IsShiny ?? next.Variant == DexVariant.Shiny;
        var key = next.Pokemon?.IsEgg == true || species == 0 ? "" : $"sprites/{(shiny ? "shiny" : "normal")}/{species}.png";
        if (key != spriteKey)
        {
            sprite?.Dispose(); sprite = null; spriteKey = key;
            if (key.Length != 0)
                try { using var stream = Context!.Assets!.Open(key); sprite = BitmapFactory.DecodeStream(stream); }
                catch (Exception ex) when (ex is IOException or Java.Lang.Exception) { sprite = null; }
        }
        Invalidate();
    }
    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas); canvas.DrawColor(AColor.Rgb(37, 59, 80));
        var scale = Math.Min(Width / 640f, Height / 400f);
        if (scale <= 0) return;
        canvas.Save(); canvas.Translate((Width - 640 * scale) / 2, (Height - 400 * scale) / 2); canvas.Scale(scale, scale);
        paint.Color = AColor.ParseColor("#E8EEDC"); canvas.DrawRect(8, 8, 632, 376, paint);
        paint.Color = AColor.ParseColor("#DCE5D1");
        for (var y = 72; y < 376; y += 8) canvas.DrawRect(10, y, 630, y + 1, paint);
        paint.Color = AColor.ParseColor("#344E65"); canvas.DrawRect(8, 8, 632, 70, paint);
        paint.Color = AColor.ParseColor("#8298A5");
        canvas.DrawRect(8, 70, 632, 73, paint);
        if (state.Pokemon is { } pokemon) DrawPokemon(canvas, pokemon);
        else if (state.Species is { } species) DrawSpecies(canvas, species);
        else
        {
            Text(canvas, "POKEMON MOVE", 36, 135, 38, "775613");
            Text(canvas, "ポケモン表示画面", 36, 195, 26);
            Text(canvas, "下画面の手持ち・ボックス・バンク・図鑑から", 36, 240, 20);
            Text(canvas, "表示するポケモンを選択してください。", 36, 275, 20);
        }
        Text(canvas, "操作は下画面で行ってください。画像は代表画像です。", 20, 391, 12, "D7E3EB");
        canvas.Restore();
    }
    private void DrawPokemon(Canvas c, PokemonSummary p)
    {
        Text(c, p.IsEgg ? "タマゴ" : $"No.{p.Species:000}  {p.Name}{(p.IsShiny ? " ★" : "")}", 20, 35, 28, "F2CF72");
        Text(c, state.Context, 20, 60, 14, "D7E3EB");
        if (p.IsEgg)
        {
            paint.Color = AColor.Rgb(242, 239, 215); c.DrawOval(new RectF(60, 100, 185, 270), paint);
            paint.Color = AColor.Rgb(87, 158, 113); c.DrawCircle(100, 150, 17, paint); c.DrawCircle(150, 213, 20, paint);
            Text(c, "孵化前のタマゴです。", 235, 180, 25); return;
        }
        Portrait(c);
        Text(c, p.Nickname, 20, 280, 20, maxWidth: 180);
        Text(c, p.Type, 20, 303, 16, "52665B", 180);
        Text(c, FormCaption(p), 20, 323, 13, "52665B", 180);
        var d = p.Details;
        Text(c, $"Lv.{p.Level}  {d?.Gender}", 220, 98, 23);
        Text(c, $"性格：{d?.Nature ?? "—"} / 特性：{d?.Ability ?? "—"}", 220, 128, 16);
        Text(c, $"もちもの：{d?.HeldItem ?? "—"}", 220, 155, 16);
        Text(c, d is null ? "—V" : $"{d.PerfectIVCount}V（個体値31）", 425, 98, 18, "775613");
        Text(c, $"親のID：{(d is null ? "—" : d.OriginalTrainerIdText)}", 20, 345, 16);
        Text(c, $"努力値合計：{Value(d?.TotalEffortValues)}", 20, 367, 16, "52665B");
        Text(c, "能力", 220, 180, 15, "52665B");
        Text(c, "実数値", 360, 180, 15, "52665B");
        Text(c, "努力値", 455, 180, 15, "52665B");
        Text(c, "個体値", 550, 180, 15, "52665B");
        var s = d?.Stats; var e = d?.EffortValues; var v = d?.IndividualValues;
        StatRow(c, "HP", 202, s?.HP, e?.HP, v?.HP, d?.CurrentHP);
        StatRow(c, "攻撃", 225, s?.Attack, e?.Attack, v?.Attack);
        StatRow(c, "防御", 248, s?.Defense, e?.Defense, v?.Defense);
        StatRow(c, "特攻", 271, s?.SpecialAttack, e?.SpecialAttack, v?.SpecialAttack);
        StatRow(c, "特防", 294, s?.SpecialDefense, e?.SpecialDefense, v?.SpecialDefense);
        StatRow(c, "素早さ", 317, s?.Speed, e?.Speed, v?.Speed);
        for (var i = 0; i < Math.Min(4, p.Moves.Count); i++)
            Text(c, $"{i + 1}. {p.Moves[i]}", 220 + (i % 2) * 200, 343 + (i / 2) * 23, 16, "52665B", 190);
    }
    private static string FormCaption(PokemonSummary p) =>
        (p.Form == 0 ? "" : $"フォルム {p.Form} ") +
        (p.Details is { HyperTrainingCount: > 0 } details ? $"とっくん {details.HyperTrainingCount}能力" : "");
    private void StatRow(Canvas c, string label, float y, int? stat, int? effort, int? iv, int? currentHP = null)
    {
        // A compact EV gauge behind the label; exact values remain in their columns.
        paint.Color = AColor.ParseColor("#CBD6BE"); c.DrawRect(220, y + 3, 335, y + 6, paint);
        paint.Color = AColor.ParseColor("#538267"); c.DrawRect(220, y + 3, 220 + 115 * Math.Clamp(effort ?? 0, 0, 255) / 255f, y + 6, paint);
        Text(c, label, 220, y, 17);
        Text(c, currentHP is { } hp ? $"{hp}/{Value(stat)}" : Value(stat), 350, y, 17, maxWidth: 100);
        Text(c, Value(effort), 470, y, 17);
        Text(c, Value(iv), 565, y, 17, iv == 31 ? "775613" : "253D37");
    }
    private void DrawSpecies(Canvas c, DexEntry p)
    {
        Text(c, $"No.{p.Species:000}  {p.Name}", 20, 35, 28, "F2CF72");
        Text(c, "収集図鑑 — 個体の能力値ではなく所持状況を表示", 20, 60, 15, "D7E3EB");
        Portrait(c);
        var variant = state.Variant;
        Text(c, variant switch { DexVariant.Normal => "通常色", DexVariant.Shiny => "色違い", _ => "通常・色違い" }, 220, 110, 22);
        Text(c, p.Count(variant) > 0 ? "● 所持" : "○ 未所持", 220, 155, 28, p.Count(variant) > 0 ? "775613" : "52665B");
        Text(c, $"手持ち：{p.Party(variant)}匹", 220, 200, 21);
        Text(c, $"ボックス：{p.Boxes(variant)}匹", 220, 240, 21);
        Text(c, $"バンク：{p.Bank(variant)}件", 220, 280, 21);
        Text(c, "現在の登録セーブとバンクの集計です。", 20, 345, 16, "52665B");
    }
    private void Portrait(Canvas c)
    {
        paint.Color = AColor.Rgb(192, 207, 183); c.DrawRoundRect(new RectF(20, 85, 200, 260), 4, 4, paint);
        if (sprite is null) { Text(c, "画像なし", 50, 180, 20, "52665B"); return; }
        paint.FilterBitmap = false;
        c.DrawBitmap(sprite, null, new RectF(25, 88, 195, 258), paint);
    }
    private void Text(Canvas c, string text, float x, float y, float size, string color = "253D37", float maxWidth = 600)
    {
        paint.Color = AColor.ParseColor("#" + color); paint.TextSize = size;
        var limit = Math.Min(maxWidth, 625 - x);
        while (text.Length > 1 && paint.MeasureText(text) > limit) text = text[..^1];
        c.DrawText(text, x, y, paint);
    }
    private static string Value(int? value) => value?.ToString() ?? "—";
    protected override void Dispose(bool disposing)
    {
        if (disposing) { sprite?.Dispose(); sprite = null; paint.Dispose(); }
        base.Dispose(disposing);
    }
}
