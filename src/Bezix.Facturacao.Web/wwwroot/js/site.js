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

    // Modo claro/escuro.
    var botaoTema = document.getElementById("alternarTema");
    function iconeTema() {
        if (!botaoTema) return;
        var escuro = document.documentElement.getAttribute("data-bs-theme") === "dark";
        botaoTema.querySelector("i").className = escuro ? "bi bi-sun" : "bi bi-moon-stars";
    }
    if (botaoTema) {
        iconeTema();
        botaoTema.addEventListener("click", function () {
            var novo = document.documentElement.getAttribute("data-bs-theme") === "dark" ? "light" : "dark";
            document.documentElement.setAttribute("data-bs-theme", novo);
            try { localStorage.setItem("bz-tema", novo); } catch (e) { }
            iconeTema();
        });
    }

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

    // Confirmação antes de acções definitivas: <button data-confirmar="Texto">.
    document.addEventListener("submit", function (e) {
        var botao = e.submitter;
        var texto = (botao && botao.getAttribute("data-confirmar")) || e.target.getAttribute("data-confirmar");
        if (texto && !window.confirm(texto)) e.preventDefault();
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
            botao.innerHTML = '<i class="bi bi-check2 me-1"></i>Copiado';
        }, function () { document.execCommand("copy"); });
    });
})();
