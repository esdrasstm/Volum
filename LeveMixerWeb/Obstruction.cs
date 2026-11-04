using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Interop.UIAutomationClient;

namespace Volum;

/// <summary>
/// A ilha está em cima de algo clicável do app da frente? (abas do navegador, menus do topo, botões,
/// campos de busca no título…). Usa a acessibilidade do Windows (UI Automation, a mesma dos leitores
/// de tela) para saber onde ficam os controles da janela.
/// Leve: só olha a faixa de cima da janela (o resto da árvore, como a página do navegador, é pulado),
/// visita no máximo 400 itens e desiste depois de 250 ms. Roda fora da thread da interface.
/// </summary>
static class Obstruction
{
    // Tipos de controle que a ilha atrapalharia (IDs do UI Automation)
    static readonly HashSet<int> Clickable = new()
    {
        50000, // Button
        50003, // ComboBox
        50004, // Edit (ex.: busca no título do VS Code)
        50005, // Hyperlink
        50007, // ListItem
        50011, // MenuItem
        50019, // TabItem (abas)
        50031, // SplitButton
    };
    const int TabItem = 50019;
    const int MaxTabs = 4;   // com mais que isso, as abas já chegam perto do meio da tela

    /// <param name="hwnd">Janela da frente.</param>
    /// <param name="area">Lugar da ilha fechada, já com uma folga ("chegar perto"), em pixels da tela.</param>
    /// <param name="bandBottom">Até onde (de cima para baixo) vale olhar.</param>
    public static bool Check(IntPtr hwnd, Rectangle area, int bandBottom)
    {
        try
        {
            var uia = new CUIAutomation();
            var root = uia.ElementFromHandle(hwnd);
            if (root == null) return false;
            var walker = uia.ControlViewWalker;
            var queue = new Queue<(IUIAutomationElement el, int depth)>();
            queue.Enqueue((root, 0));
            var sw = Stopwatch.StartNew();
            int visited = 0, tabs = 0;

            while (queue.Count > 0 && visited < 400 && sw.ElapsedMilliseconds < 250)
            {
                var (el, depth) = queue.Dequeue();
                var child = walker.GetFirstChildElement(el);
                while (child != null && visited < 400)
                {
                    visited++;
                    var r = child.CurrentBoundingRectangle;
                    var rect = Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom);
                    if (rect.Width > 0 && rect.Height > 0 && rect.Top <= bandBottom)   // só o que fica lá em cima
                    {
                        int type = child.CurrentControlType;
                        if (type == TabItem && ++tabs > MaxTabs) return true;
                        // Controle clicável embaixo da ilha (os gigantes, do tamanho da janela, não contam)
                        if (Clickable.Contains(type) && rect.IntersectsWith(area) && rect.Width < area.Width * 3) return true;
                        if (depth < 14) queue.Enqueue((child, depth + 1));
                    }
                    child = walker.GetNextSiblingElement(child);
                }
            }
        }
        catch { /* app fechou ou não responde: a ilha fica como está */ }
        return false;
    }
}
