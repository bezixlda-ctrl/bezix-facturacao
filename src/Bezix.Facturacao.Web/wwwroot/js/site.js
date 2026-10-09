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
})();
