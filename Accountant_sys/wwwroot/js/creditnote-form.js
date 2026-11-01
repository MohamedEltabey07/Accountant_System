(function () {
    'use strict';

    window.initCreditNoteForm = function (config) {
        var form = document.getElementById(config.formId);
        var submitBtn = document.getElementById(config.submitBtnId);
        var grandTotal = document.getElementById(config.grandTotalId);

        function formatNumber(value) {
            return Number(value || 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        }

        function eachRow(callback) {
            form.querySelectorAll('tr').forEach(function (tr) {
                var check = tr.querySelector('.line-check');
                var qty = tr.querySelector('.line-qty');
                var product = tr.querySelector('.line-product');
                if (check && qty && product) {
                    callback({
                        check: check,
                        qty: qty,
                        product: product,
                        max: parseFloat(qty.getAttribute('max')) || 0,
                        unitPrice: 0,
                        totalCell: tr.querySelector('.line-total'),
                        errorCell: tr.querySelector('.line-error')
                    });
                }
            });
        }

        function unitPriceFor(qty) {
            return parseFloat(qty.closest('tr').getAttribute('data-unit-price') || 0);
        }

        function refreshTotals() {
            var total = 0;
            eachRow(function (row) {
                if (row.check.checked) {
                    total += (parseFloat(row.qty.value) || 0) * unitPriceFor(row.qty);
                }
            });
            grandTotal.textContent = formatNumber(total);
        }

        function validateRow(row) {
            row.errorCell.textContent = '';
            if (!row.check.checked) return;
            var value = parseFloat(row.qty.value) || 0;
            if (value <= 0) {
                row.errorCell.textContent = 'أدخل كمية أكبر من صفر';
            } else if (value > row.max + 0.005) {
                row.errorCell.textContent = 'تجاوز الحد الأقصى (' + row.max + ')';
            }
        }

        function stripUncheckedNames() {
            var index = 0;
            eachRow(function (row) {
                if (row.check.checked && (parseFloat(row.qty.value) || 0) > 0) {
                    row.product.name = 'Items[' + index + '].ProductId';
                    row.qty.name = 'Items[' + index + '].Quantity';
                    index++;
                } else {
                    row.product.removeAttribute('name');
                    row.qty.removeAttribute('name');
                }
            });
            return index;
        }

        form.addEventListener('submit', function (e) {
            e.preventDefault();
            var lines = stripUncheckedNames();
            if (lines === 0) {
                Swal.fire({ icon: 'warning', title: 'خطأ', text: 'حدد صنفًا واحدًا على الأقل للإرجاع وأدخل الكمية.', confirmButtonText: 'حسناً' });
                return;
            }
            var invalidRow = null;
            eachRow(function (row) {
                validateRow(row);
                if (!invalidRow && row.errorCell.textContent) invalidRow = row.errorCell.textContent;
            });
            var reason = form.querySelector('[name="Reason"]');
            if (reason && !reason.value.trim()) {
                if (window.jQuery && window.jQuery(form).valid) window.jQuery(form).valid();
                Swal.fire({ icon: 'warning', title: 'خطأ', text: 'أدخل سبب الإرجاع.', confirmButtonText: 'حسناً' });
                return;
            }
            if (invalidRow) {
                Swal.fire({ icon: 'warning', title: 'كمية غير صحيحة', text: invalidRow, confirmButtonText: 'حسناً' });
                return;
            }
            Swal.fire({
                icon: 'question',
                title: config.confirmTitle,
                text: config.confirmText,
                showCancelButton: true,
                confirmButtonText: config.confirmButtonText,
                cancelButtonText: 'إلغاء',
                confirmButtonColor: '#0f3460'
            }).then(function (result) {
                if (result.isConfirmed) {
                    form.submit();
                }
            });
        });

        form.querySelectorAll('tr').forEach(function (tr) {
            var check = tr.querySelector('.line-check');
            var qty = tr.querySelector('.line-qty');
            if (!check || !qty) return;
            var row = { check: check, qty: qty, max: parseFloat(qty.getAttribute('max')) || 0 };
            check.addEventListener('change', function () {
                qty.disabled = !check.checked;
                if (check.checked && !qty.value) qty.value = row.max.toFixed(2);
                if (!check.checked) qty.value = '';
                tr.querySelector('.line-error').textContent = '';
                refreshTotals();
            });
            qty.addEventListener('input', function () {
                var value = parseFloat(qty.value) || 0;
                tr.querySelector('.line-total').textContent = formatNumber(value * unitPriceFor(qty));
                validateRow({ check: check, qty: qty, max: row.max, errorCell: tr.querySelector('.line-error') });
                refreshTotals();
            });
        });

        refreshTotals();
    };
})();
