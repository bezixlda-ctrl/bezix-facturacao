// Painel: gráficos carregados depois da página (ApexCharts).
// As cores vêm dos tokens CSS no momento de desenhar; ao mudar de tema ("bz:tema") os gráficos são redesenhados.
(function () {
    "use strict";
    if (typeof ApexCharts === "undefined") return;

    var formato = new Intl.NumberFormat("pt-PT", { minimumFractionDigits: 2, maximumFractionDigits: 2, useGrouping: "always" });
    var mt = function (v) { return formato.format(v).replace(/\s| | /g, ".") + " MT"; };
    var curto = function (v) {
        if (Math.abs(v) >= 1e6) return (v / 1e6).toFixed(1).replace(".", ",") + " M";
        if (Math.abs(v) >= 1e3) return Math.round(v / 1e3) + " mil";
        return Math.round(v).toString();
    };
    var reduzirMovimento = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    function tokens() {
        var raiz = getComputedStyle(document.documentElement);
        var cor = function (nome, alternativa) { return misturar((raiz.getPropertyValue(nome) || alternativa).trim()); };
        var escuro = document.documentElement.getAttribute("data-bs-theme") === "dark";
        return {
            escuro: escuro,
            cor: cor,
            // Em escuro, a linha da marca usa a versão clareada para ter contraste sobre a superfície.
            primaria: cor(escuro ? "--bz-primaria-texto" : "--bz-primaria", "#0F5C8C"),
            sucesso: cor("--bz-sucesso", "#15805A"),
            texto: cor("--bz-texto-suave", "#5A6676"),
            grelha: cor("--bz-borda", "#E0E5EB"),
            superficie: cor("--bz-superficie", "#FFFFFF"),
        };
    }

    function base(t) {
        return {
            chart: {
                fontFamily: "Manrope, system-ui, sans-serif", foreColor: t.texto, toolbar: { show: false }, zoom: { enabled: false },
                background: "transparent", animations: { enabled: !reduzirMovimento, speed: 350 },
            },
            theme: { mode: t.escuro ? "dark" : "light" },
            grid: { borderColor: t.grelha, strokeDashArray: 3, padding: { left: 4, right: 8 } },
            dataLabels: { enabled: false },
            tooltip: { theme: t.escuro ? "dark" : "light", y: { formatter: mt } },
        };
    }

    var graficos = [], dados = null;

    function limpar() {
        graficos.forEach(function (g) { g.destroy(); });
        graficos = [];
        document.querySelectorAll(".spark").forEach(function (el) { el.innerHTML = ""; });
    }

    function desenhar() {
        if (!dados) return;
        limpar();
        var d = dados, t = tokens(), b = base(t);

        var elPrincipal = document.getElementById("graficoPrincipal");
        if (elPrincipal) {
            var series = [], cores = [];
            if (d.facturado) { series.push({ name: "Facturado", data: d.facturado }); cores.push(t.primaria); }
            if (d.recebido) { series.push({ name: "Recebido", data: d.recebido }); cores.push(t.sucesso); }
            var principal = new ApexCharts(elPrincipal, Object.assign({}, b, {
                chart: Object.assign({}, b.chart, { type: "area", height: 260 }),
                series: series,
                colors: cores,
                stroke: { curve: "monotoneCubic", width: 2.25 },
                fill: { type: "gradient", gradient: { shadeIntensity: 0, opacityFrom: t.escuro ? 0.32 : 0.22, opacityTo: 0, stops: [0, 95] } },
                markers: { size: 0, hover: { size: 4, strokeColors: t.superficie } },
                xaxis: {
                    categories: d.rotulos, tickAmount: Math.min(d.rotulos.length, window.innerWidth < 576 ? 4 : 12),
                    labels: { rotate: 0, hideOverlappingLabels: true },
                    axisBorder: { show: false }, axisTicks: { show: false }, tooltip: { enabled: false },
                },
                yaxis: { labels: { formatter: curto } },
                legend: { show: false },
            }));
            principal.render();
            graficos.push(principal);
        }

        var elDonut = document.getElementById("graficoAntiguidade");
        if (elDonut && d.antiguidade && d.antiguidade.some(function (v) { return v > 0; })) {
            // Rampa de aviso para erro: quanto mais antigo, mais vermelho.
            var aviso = t.cor("--bz-aviso", "#A65F06"), erro = t.cor("--bz-erro", "#BF2D23");
            var donut = new ApexCharts(elDonut, Object.assign({}, b, {
                chart: Object.assign({}, b.chart, { type: "donut", height: 270 }),
                series: d.antiguidade,
                labels: ["0–30 dias", "31–60 dias", "61–90 dias", "Mais de 90 dias"],
                colors: [
                    misturar("color-mix(in srgb, " + aviso + " 55%, " + t.superficie + ")"), aviso,
                    misturar("color-mix(in srgb, " + erro + " 70%, " + aviso + ")"), erro,
                ],
                legend: { position: "bottom", fontSize: "13px", markers: { size: 5 } },
                plotOptions: { pie: { donut: { size: "70%", labels: { show: true, total: { show: true, label: "Vencido", color: t.texto, formatter: function (w) {
                    return curto(w.globals.seriesTotals.reduce(function (a, c) { return a + c; }, 0)) + " MT";
                } } } } } },
                stroke: { width: 2, colors: [t.superficie] },
            }));
            donut.render();
            graficos.push(donut);
        }

        document.querySelectorAll(".spark").forEach(function (el) {
            var serie = d[el.dataset.serie];
            if (!serie) return;
            var cor = el.dataset.cor === "--bz-primaria" ? t.primaria : t.cor(el.dataset.cor || "--bz-primaria", t.primaria);
            var spark = new ApexCharts(el, {
                chart: { type: "area", height: 40, sparkline: { enabled: true }, animations: { enabled: false } },
                series: [{ data: serie }],
                stroke: { curve: "monotoneCubic", width: 1.75 },
                colors: [cor],
                fill: { type: "gradient", gradient: { shadeIntensity: 0, opacityFrom: 0.22, opacityTo: 0 } },
                tooltip: { enabled: false },
            });
            spark.render();
            graficos.push(spark);
        });
    }

    // O ApexCharts não aceita color-mix() nem var(): o próprio browser resolve a cor e o canvas devolve-a em hexadecimal.
    var tela = document.createElement("canvas").getContext("2d");
    function misturar(css) {
        if (/^#[0-9a-f]{3,8}$/i.test(css)) return css;
        var el = document.createElement("span");
        el.style.color = css;
        document.body.appendChild(el);
        var rgb = getComputedStyle(el).color;
        el.remove();
        tela.fillStyle = rgb;
        return tela.fillStyle;
    }

    function carregar(periodo) {
        fetch("?handler=Graficos&periodo=" + periodo, { headers: { "Accept": "application/json" } })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (d) { if (d) { dados = d; desenhar(); } });
    }

    var botoes = document.getElementById("periodoGrafico");
    if (botoes) botoes.addEventListener("click", function (e) {
        var bt = e.target.closest("[data-periodo]");
        if (!bt) return;
        botoes.querySelectorAll("button").forEach(function (x) {
            x.classList.toggle("active", x === bt);
            x.setAttribute("aria-pressed", x === bt ? "true" : "false");
        });
        carregar(bt.dataset.periodo);
    });

    document.addEventListener("bz:tema", desenhar);
    carregar("Mensal");
})();
