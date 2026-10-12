// Bezix Facturação — comportamento comum.

(function () {
    "use strict";

    // Mensagens (toasts) vindas do servidor.
    function mostrarToasts(raiz) {
        raiz.querySelectorAll(".toast:not(.show)").forEach(function (el) {
            bootstrap.Toast.getOrCreateInstance(el).show();
        });
    }
    document.addEventListener("DOMContentLoaded", function () { mostrarToasts(document); });
    document.body.addEventListener("htmx:afterSwap", function (e) { mostrarToasts(e.target); });

    // Aspecto: claro, escuro ou automático (segue o sistema). Avisa a página ("bz:tema") para redesenhar gráficos.
    var raizHtml = document.documentElement;
    var sistemaEscuro = window.matchMedia("(prefers-color-scheme: dark)");
    var iconesTema = { light: "ph-sun", dark: "ph-moon", auto: "ph-circle-half" };
    function aplicarTema(escolha) {
        var tema = escolha === "auto" ? (sistemaEscuro.matches ? "dark" : "light") : escolha;
        var mudou = raizHtml.getAttribute("data-bs-theme") !== tema;
        raizHtml.setAttribute("data-bs-theme", tema);
        raizHtml.setAttribute("data-tema-escolha", escolha);
        document.querySelectorAll("[data-tema]").forEach(function (b) {
            b.setAttribute("aria-checked", b.getAttribute("data-tema") === escolha ? "true" : "false");
        });
        var icone = document.querySelector("#botaoTema i");
        if (icone) icone.className = "ph " + iconesTema[escolha];
        if (mudou) document.dispatchEvent(new CustomEvent("bz:tema", { detail: { tema: tema } }));
    }
    aplicarTema(raizHtml.getAttribute("data-tema-escolha") || "auto");
    document.addEventListener("click", function (e) {
        var b = e.target.closest("[data-tema]");
        if (!b) return;
        var escolha = b.getAttribute("data-tema");
        try { localStorage.setItem("bz-tema", escolha); } catch (er) { }
        aplicarTema(escolha);
    });
    sistemaEscuro.addEventListener("change", function () {
        if (raizHtml.getAttribute("data-tema-escolha") === "auto") aplicarTema("auto");
    });

    // Pesquisa global: Ctrl+K (ou Cmd+K) foca; Enter abre o primeiro resultado; Esc fecha.
    var pesquisa = document.getElementById("pesquisaGlobal");
    var resultados = document.getElementById("resultadosPesquisa");
    if (pesquisa && resultados) {
        document.addEventListener("keydown", function (e) {
            if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
                e.preventDefault();
                pesquisa.focus();
                pesquisa.select();
            }
        });
        pesquisa.addEventListener("keydown", function (e) {
            if (e.key === "Enter") {
                var primeiro = resultados.querySelector("a");
                if (primeiro) { e.preventDefault(); window.location.href = primeiro.href; }
            } else if (e.key === "Escape") {
                pesquisa.value = "";
                resultados.innerHTML = "";
            } else if (e.key === "ArrowDown") {
                var link = resultados.querySelector("a");
                if (link) { e.preventDefault(); link.focus(); }
            }
        });
        resultados.addEventListener("keydown", function (e) {
            var links = Array.prototype.slice.call(resultados.querySelectorAll("a"));
            var i = links.indexOf(document.activeElement);
            if (e.key === "ArrowDown" && i < links.length - 1) { e.preventDefault(); links[i + 1].focus(); }
            if (e.key === "ArrowUp") { e.preventDefault(); (i > 0 ? links[i - 1] : pesquisa).focus(); }
            if (e.key === "Escape") { resultados.innerHTML = ""; pesquisa.focus(); }
        });
        document.addEventListener("click", function (e) {
            if (!e.target.closest(".pesquisa-global")) resultados.innerHTML = "";
        });
    }

    // Confirmação antes de acções definitivas: <button data-confirmar="Texto"> (ou no <form>).
    // Janela da própria aplicação (segue o tema), com o botão a dizer a acção; vermelho para acções destrutivas.
    var janelaConfirmar = null, pedidoConfirmar = null;
    function criarJanelaConfirmar() {
        var el = document.createElement("div");
        el.className = "modal fade";
        el.tabIndex = -1;
        el.setAttribute("aria-labelledby", "confirmarTitulo");
        el.setAttribute("aria-hidden", "true");
        el.innerHTML =
            '<div class="modal-dialog modal-dialog-centered"><div class="modal-content janela-confirmar">' +
            '<div class="modal-body"><div class="d-flex gap-3"><span class="janela-confirmar-icone" aria-hidden="true"><i class="ph ph-warning-circle"></i></span>' +
            '<div><h2 class="h5 mb-1" id="confirmarTitulo">Confirmar</h2><p class="mb-0" data-texto></p></div></div></div>' +
            '<div class="modal-footer"><button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Cancelar</button>' +
            '<button type="button" class="btn btn-primary" data-confirmar-ok>Confirmar</button></div></div></div>';
        document.body.appendChild(el);
        el.querySelector("[data-confirmar-ok]").addEventListener("click", function () {
            var p = pedidoConfirmar;
            pedidoConfirmar = null;
            janelaConfirmar.hide();
            if (!p) return;
            p.form.dataset.confirmado = "1";
            if (p.form.requestSubmit) p.form.requestSubmit(p.botao || undefined); else p.form.submit();
        });
        el.addEventListener("shown.bs.modal", function () { el.querySelector("[data-confirmar-ok]").focus(); });
        return new bootstrap.Modal(el);
    }
    document.addEventListener("submit", function (e) {
        var form = e.target, botao = e.submitter;
        if (form.dataset.confirmado === "1") { delete form.dataset.confirmado; return; }
        var texto = (botao && botao.getAttribute("data-confirmar")) || form.getAttribute("data-confirmar");
        if (!texto) return;
        e.preventDefault();
        if (typeof bootstrap === "undefined") { if (window.confirm(texto)) { form.dataset.confirmado = "1"; form.requestSubmit(botao || undefined); } return; }
        janelaConfirmar = janelaConfirmar || criarJanelaConfirmar();
        var el = janelaConfirmar._element;
        var accao = ((botao && botao.innerText) || "Confirmar").trim() || "Confirmar";
        var perigo = /apagar|anular|revogar|remover|rejeitar|desactivar|fechar/i.test(accao + " " + texto) ||
            (botao && /danger/.test(botao.className));
        el.querySelector("[data-texto]").textContent = texto;
        el.querySelector("#confirmarTitulo").textContent = accao.replace(/\s+/g, " ");
        var ok = el.querySelector("[data-confirmar-ok]");
        ok.textContent = accao.replace(/\s+/g, " ");
        ok.className = "btn " + (perigo ? "btn-danger" : "btn-primary");
        el.querySelector(".janela-confirmar-icone").classList.toggle("perigo", !!perigo);
        pedidoConfirmar = { form: form, botao: botao };
        janelaConfirmar.show();
    });

    // Pré-visualização de imagem antes do envio: <input type=file data-previa="#id-img">.
    document.querySelectorAll("input[type=file][data-previa]").forEach(function (input) {
        input.addEventListener("change", function () {
            var img = document.querySelector(input.getAttribute("data-previa"));
            if (!img || !input.files || !input.files[0]) return;
            img.src = URL.createObjectURL(input.files[0]);
            img.hidden = false;
        });
    });

    // Atalhos de período nos relatórios: <button data-periodo-de="AAAA-MM-DD" data-periodo-ate="AAAA-MM-DD">.
    document.addEventListener("click", function (e) {
        var botao = e.target.closest("[data-periodo-de]");
        if (!botao || !botao.form) return;
        botao.form.elements["De"].value = botao.getAttribute("data-periodo-de");
        botao.form.elements["Ate"].value = botao.getAttribute("data-periodo-ate");
    });

    // Copiar para a área de transferência: <button data-copiar="#campo">.
    document.addEventListener("click", function (e) {
        var botao = e.target.closest("[data-copiar]");
        if (!botao) return;
        var campo = document.querySelector(botao.getAttribute("data-copiar"));
        if (!campo) return;
        campo.select();
        (navigator.clipboard ? navigator.clipboard.writeText(campo.value) : Promise.reject()).then(function () {
            botao.innerHTML = '<i class="ph ph-check me-1"></i>Copiado';
        }, function () { document.execCommand("copy"); });
    });
})();
