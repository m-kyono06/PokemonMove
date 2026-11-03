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
        base.OnDraw(canvas); canvas.DrawColor(AColor.Rgb(21, 43, 54));
        var scale = Math.Min(Width / 640f, Height / 400f);
        if (scale <= 0) return;
        canvas.Save(); canvas.Translate((Width - 640 * scale) / 2, (Height - 400 * scale) / 2); canvas.Scale(scale, scale);
        if (state.Pokemon is { } pokemon) DrawPokemon(canvas, pokemon);
        else if (state.Species is { } species) DrawSpecies(canvas, species);
        else
        {
            Text(canvas, "POKEMON MOVE", 36, 135, 38, "61DFC0");
            Text(canvas, "ポケモン表示画面", 36, 195, 26);
            Text(canvas, "下画面の手持ち・ボックス・バンク・図鑑から", 36, 240, 20);
            Text(canvas, "表示するポケモンを選択してください。", 36, 275, 20);
        }
        Text(canvas, "操作は下画面で行ってください。画像は代表画像です。", 20, 386, 12, "AFC2CD");
        canvas.Restore();
    }
    private void DrawPokemon(Canvas c, PokemonSummary p)
    {
        Text(c, p.IsEgg ? "タマゴ" : $"No.{p.Species:000}  {p.Name}{(p.IsShiny ? " ★" : "")}", 20, 35, 28, "61DFC0");
        Text(c, state.Context, 20, 60, 14, "AFC2CD");
        if (p.IsEgg)
        {
            paint.Color = AColor.Rgb(242, 239, 215); c.DrawOval(new RectF(60, 100, 185, 270), paint);
            paint.Color = AColor.Rgb(87, 158, 113); c.DrawCircle(100, 150, 17, paint); c.DrawCircle(150, 213, 20, paint);
            Text(c, "孵化前のタマゴです。", 235, 180, 25); return;
        }
        Portrait(c);
        Text(c, p.Nickname, 20, 280, 20);
        Text(c, p.Type, 20, 306, 16, "AFC2CD");
        Text(c, p.Form == 0 ? "" : $"フォルム {p.Form}（画像は代表）", 20, 332, 13, "AFC2CD");
        var d = p.Details;
        Text(c, $"Lv.{p.Level}  {d?.Gender}", 220, 98, 23);
        Text(c, $"性格：{d?.Nature ?? "—"} / 特性：{d?.Ability ?? "—"}", 220, 128, 16);
        Text(c, $"もちもの：{d?.HeldItem ?? "—"}", 220, 155, 16);
        var s = d?.Stats;
        Text(c, $"HP  {(d?.CurrentHP is { } hp ? hp + " / " : "")}{Value(s?.HP)}", 220, 190, 20);
        Text(c, $"攻撃  {Value(s?.Attack)}", 420, 190, 20);
        Text(c, $"防御  {Value(s?.Defense)}", 220, 225, 20);
        Text(c, $"特攻  {Value(s?.SpecialAttack)}", 420, 225, 20);
        Text(c, $"特防  {Value(s?.SpecialDefense)}", 220, 260, 20);
        Text(c, $"素早さ  {Value(s?.Speed)}", 420, 260, 20);
        for (var i = 0; i < Math.Min(4, p.Moves.Count); i++)
            Text(c, $"{i + 1}. {p.Moves[i]}", 220 + (i % 2) * 200, 305 + (i / 2) * 30, 16, "AFC2CD", 190);
    }
    private void DrawSpecies(Canvas c, DexEntry p)
    {
        Text(c, $"No.{p.Species:000}  {p.Name}", 20, 35, 28, "61DFC0");
        Text(c, "収集図鑑 — 個体の能力値ではなく所持状況を表示", 20, 60, 15, "AFC2CD");
        Portrait(c);
        var variant = state.Variant;
        Text(c, variant switch { DexVariant.Normal => "通常色", DexVariant.Shiny => "色違い", _ => "通常・色違い" }, 220, 110, 22);
        Text(c, p.Count(variant) > 0 ? "● 所持" : "○ 未所持", 220, 155, 28, p.Count(variant) > 0 ? "61DFC0" : "AFC2CD");
        Text(c, $"手持ち：{p.Party(variant)}匹", 220, 200, 21);
        Text(c, $"ボックス：{p.Boxes(variant)}匹", 220, 240, 21);
        Text(c, $"バンク：{p.Bank(variant)}件", 220, 280, 21);
        Text(c, "現在の登録セーブとバンクの集計です。", 20, 345, 16, "AFC2CD");
    }
    private void Portrait(Canvas c)
    {
        paint.Color = AColor.Rgb(35, 62, 75); c.DrawRoundRect(new RectF(20, 85, 200, 260), 18, 18, paint);
        if (sprite is null) { Text(c, "画像なし", 50, 180, 20, "AFC2CD"); return; }
        paint.FilterBitmap = false;
        c.DrawBitmap(sprite, null, new RectF(25, 88, 195, 258), paint);
    }
    private void Text(Canvas c, string text, float x, float y, float size, string color = "FFFFFF", float maxWidth = 600)
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
