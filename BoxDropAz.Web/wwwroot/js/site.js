(function () {
    'use strict';

    // Bootstrap client-side validation styling for forms opting in with .needs-validation
    document.querySelectorAll('.needs-validation').forEach(function (form) {
        form.addEventListener('submit', function (event) {
            if (!form.checkValidity()) {
                event.preventDefault();
                event.stopPropagation();
            }
            form.classList.add('was-validated');
        }, false);
    });

    // Guard against double submission on payment and booking forms, where a second POST
    // could create a duplicate order.
    document.querySelectorAll('form[data-submit-once]').forEach(function (form) {
        form.addEventListener('submit', function () {
            var button = form.querySelector('button[type="submit"]');
            if (!button || button.dataset.submitting === '1') {
                return;
            }
            button.dataset.submitting = '1';
            button.disabled = true;
            button.dataset.originalText = button.innerHTML;
            button.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span>Working...';
        });
    });

    // Auto-dismiss transient alerts so a success banner doesn't linger over the page
    document.querySelectorAll('[data-auto-dismiss]').forEach(function (alert) {
        setTimeout(function () {
            alert.classList.remove('show');
        }, 6000);
    });

    // Homepage "how many totes?" quiz
    var quiz = document.getElementById('toteQuiz');
    if (quiz) {
        var labels = {
            studio: 'Studio — 20 totes, 1 custom-fit dolly',
            small: '1–2 Bedroom — 35 totes, 2 custom-fit dollies',
            medium: '2–3 Bedroom (most popular) — 50 totes, 2 dollies',
            large: '3–4 Bedroom — 75 totes, 3 custom-fit dollies',
            xlarge: '4–5 Bedroom — 100 totes, 4 custom-fit dollies'
        };
        var region = quiz.getAttribute('data-region') || 'phoenix';
        var result = document.getElementById('toteQuizResult');
        var message = document.getElementById('toteQuizMessage');
        var book = document.getElementById('toteQuizBook');

        quiz.querySelectorAll('.bd-quiz-option').forEach(function (btn) {
            btn.addEventListener('click', function () {
                quiz.querySelectorAll('.bd-quiz-option').forEach(function (b) {
                    b.classList.remove('is-selected');
                });
                btn.classList.add('is-selected');

                var pkg = btn.getAttribute('data-package') || '';
                result.classList.remove('d-none');

                if (!pkg) {
                    message.textContent = 'No problem — compare packages below, or start one size up if you are between sizes.';
                    book.classList.add('d-none');
                    var packages = document.getElementById('packages');
                    if (packages) {
                        packages.scrollIntoView({ behavior: 'smooth', block: 'start' });
                    }
                    return;
                }

                message.textContent = 'Recommended: ' + (labels[pkg] || pkg) + '.';
                book.classList.remove('d-none');
                book.setAttribute('href', '/Booking?package=' + encodeURIComponent(pkg) + '&region=' + encodeURIComponent(region));
                book.textContent = 'Choose this bundle';
            });
        });
    }

    // Soft reveal for homepage visual steps
    if ('IntersectionObserver' in window) {
        var reveal = document.querySelectorAll('.bd-visual-step, .bd-testimonial, .bd-audience-item');
        if (reveal.length) {
            var io = new IntersectionObserver(function (entries) {
                entries.forEach(function (entry) {
                    if (entry.isIntersecting) {
                        entry.target.classList.add('is-visible');
                        io.unobserve(entry.target);
                    }
                });
            }, { threshold: 0.12 });
            reveal.forEach(function (el) {
                el.classList.add('bd-reveal');
                io.observe(el);
            });
        }
    }
})();
