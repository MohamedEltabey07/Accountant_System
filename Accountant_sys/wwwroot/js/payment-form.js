(function () {
    'use strict';

    window.initPaymentForm = function (config) {
        var partySelect = document.getElementById(config.partySelectId);
        var openSection = document.getElementById(config.openSectionId);
        var tableBody = document.getElementById(config.tableBodyId);
        var emptyState = document.getElementById(config.emptyStateId);
        var loadingState = document.getElementById(config.loadingStateId);
        var partyNameLabel = document.getElementById(config.partyNameId);
        var amountInput = document.getElementById(config.amountInputId);
        var submitBtn = document.getElementById(config.submitBtnId);
        var summary = {
            amount: document.getElementById(config.summaryAmountId),
            allocated: document.getElementById(config.summaryAllocatedId),
            remaining: document.getElementById(config.summaryRemainingId),
            badge: document.getElementById(config.summaryBadgeId)
        };

        var rows = [];

        function formatNumber(value) {
            return Number(value || 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        }

        function amount() {
            return parseFloat(amountInput.value) || 0;
        }

        function partyName() {
            if (partySelect.selectedIndex <= 0) return '';
            return partySelect.options[partySelect.selectedIndex].text;
        }

        function checkedRows() {
            return rows.filter(function (row) { return row.checkbox.checked; });
        }

        function setRowValue(row, value, isAuto) {
            row.input.value = value > 0 ? value.toFixed(2) : '';
            row.input.dataset.auto = isAuto ? '1' : '0';
        }

        function rowValue(row) {
            return parseFloat(row.input.value) || 0;
        }

        // Distributes the payment amount over the checked rows in order:
        // rows marked auto are re-filled sequentially; manually-set rows are kept
        // and clamped to their remaining if they exceed it.
        function redistributeAll() {
            var amountVal = amount();
            if (amountVal <= 0) {
                updateSummary();
                return;
            }

            var manualSum = 0;
            checkedRows().forEach(function (row) {
                if (row.input.dataset.auto !== '1') {
                    var v = rowValue(row);
                    if (v > row.remaining + 0.005) {
                        setRowValue(row, row.remaining, true);
                        v = row.remaining;
                    }
                    manualSum += v;
                }
            });

            var pool = Math.max(0, amountVal - manualSum);
            checkedRows().forEach(function (row) {
                if (row.input.dataset.auto === '1') {
                    var v = Math.min(row.remaining, pool);
                    pool -= v;
                    setRowValue(row, v, true);
                }
            });

            rebuildAllocationNames();
            updateSummary();
        }

        function updateSummary() {
            var amountVal = amount();
            var allocated = 0;
            var checked = 0;
            var closed = 0;
            var hasError = false;
            rows.forEach(function (row) {
                if (row.checkbox.checked) {
                    checked++;
                    var v = rowValue(row);
                    allocated += v;
                    if (v > row.remaining + 0.005) hasError = true;
                    if (v >= row.remaining - 0.005) closed++;
                }
            });
            var left = amountVal - allocated;

            summary.amount.textContent = formatNumber(amountVal);
            summary.allocated.textContent = formatNumber(allocated);
            summary.remaining.textContent = formatNumber(left);

            if (checked === 0 && amountVal === 0) {
                summary.badge.className = 'badge bg-secondary';
                summary.badge.textContent = 'لم يدخل';
            } else if (checked === 0) {
                summary.badge.className = 'badge bg-warning text-dark';
                summary.badge.textContent = 'اختر فاتورة واحدة على الأقل';
            } else if (hasError) {
                summary.badge.className = 'badge bg-danger';
                summary.badge.textContent = 'يوجد مبلغ توزيع يتجاوز المتبقي على فاتورة';
            } else if (Math.abs(left) < 0.005) {
                summary.badge.className = 'badge bg-success';
                summary.badge.textContent = 'متوازن تماماً';
                if (closed === checked) {
                    summary.badge.textContent = 'متوازن تماماً — الفواتير المحددة مسددة بالكامل';
                }
            } else if (left > 0) {
                summary.badge.className = 'badge bg-warning text-dark';
                summary.badge.textContent = 'متبقي ' + formatNumber(left) + ' لم يتم توزيعه';
            } else {
                summary.badge.className = 'badge bg-danger';
                summary.badge.textContent = 'التوزيع يتجاوز مبلغ الدفعة بمقدار ' + formatNumber(-left);
            }

            updateSubmitState(amountVal, allocated, checked);
        }

        function updateSubmitState(amountVal, allocated, checked) {
            var parent = submitBtn.closest('form');
            var partyOk = partySelect.value && partySelect.value !== '';
            var amountOk = amountVal > 0;
            var dateOk = config.dateInputId ? !!document.getElementById(config.dateInputId).value : true;
            var balanced = Math.abs(amountVal - allocated) < 0.005 && allocated > 0;
            var valid = parent.checkValidity();
            submitBtn.disabled = !(partyOk && amountOk && dateOk && balanced && valid && checked > 0);
        }

        function clampRow(row) {
            var v = rowValue(row);
            if (v > row.remaining + 0.005) {
                setRowValue(row, row.remaining, true);
            }
        }

        function rebuildAllocationNames() {
            var index = 0;
            rows.forEach(function (row) {
                if (row.checkbox.checked) {
                    row.idInput.name = 'Allocations[' + index + '].InvoiceId';
                    row.input.name = 'Allocations[' + index + '].AllocatedAmount';
                    index++;
                } else {
                    row.idInput.removeAttribute('name');
                    row.input.removeAttribute('name');
                }
            });
        }

        function buildRow(invoice) {
            var invoiceId = invoice.invoiceId ?? invoice.InvoiceId;
            var invoiceNumber = invoice.invoiceNumber ?? invoice.InvoiceNumber;
            var invoiceDate = invoice.transactionDate ?? invoice.TransactionDate;
            var total = parseFloat(invoice.totalAmount ?? invoice.TotalAmount) || 0;
            var paid = parseFloat(invoice.paidAmount ?? invoice.PaidAmount) || 0;
            var remaining = parseFloat(invoice.remainingBalance ?? invoice.RemainingBalance) || 0;

            var tr = document.createElement('tr');

            var tdCheck = document.createElement('td');
            var check = document.createElement('input');
            check.type = 'checkbox';
            check.className = 'form-check-input fs-5';
            tdCheck.appendChild(check);

            var idInput = document.createElement('input');
            idInput.type = 'hidden';
            idInput.value = invoiceId;

            var tdNumber = document.createElement('td');
            tdNumber.appendChild(idInput);
            var strong = document.createElement('span');
            strong.className = 'fw-bold';
            strong.textContent = invoiceNumber;
            tdNumber.appendChild(strong);

            var tdDate = document.createElement('td');
            var parsedDate = invoiceDate ? new Date(invoiceDate) : null;
            tdDate.textContent = parsedDate && !isNaN(parsedDate.getTime()) ? parsedDate.toLocaleDateString('ar-EG') : '-';

            var tdTotal = document.createElement('td');
            tdTotal.textContent = formatNumber(total);

            var tdPaid = document.createElement('td');
            tdPaid.textContent = formatNumber(paid);

            var tdRemaining = document.createElement('td');
            tdRemaining.textContent = formatNumber(remaining);
            tdRemaining.className = 'fw-bold text-danger';

            var tdAmount = document.createElement('td');
            var input = document.createElement('input');
            input.type = 'number';
            input.step = '0.01';
            input.min = '0.00';
            input.className = 'form-control form-control-sm allocated-input text-center';
            input.disabled = true;
            tdAmount.appendChild(input);

            var tdError = document.createElement('td');
            tdError.className = 'text-danger small';
            tdError.style.maxWidth = '160px';

            tr.appendChild(tdCheck);
            tr.appendChild(tdNumber);
            tr.appendChild(tdDate);
            tr.appendChild(tdTotal);
            tr.appendChild(tdPaid);
            tr.appendChild(tdRemaining);
            tr.appendChild(tdAmount);
            tr.appendChild(tdError);

            var row = {
                checkbox: check,
                idInput: idInput,
                input: input,
                remaining: remaining,
                errorCell: tdError
            };

            check.addEventListener('change', function () {
                row.input.disabled = !check.checked;
                if (check.checked) {
                    row.input.dataset.auto = '1';
                    redistributeAll();
                } else {
                    row.input.value = '';
                    row.idInput.removeAttribute('name');
                    row.input.removeAttribute('name');
                    redistributeAll();
                }
            });

            input.addEventListener('input', function () {
                input.dataset.auto = '0';
                clampRow(row);
                redistributeAll();
            });

            return row;
        }

        function clearRows() {
            rows.forEach(function (row) {
                row.checkbox.checked = false;
                rebuildAllocationNames();
            });
            rows = [];
            tableBody.textContent = '';
        }

        function partyChanged() {
            var partyId = partySelect.value;
            openSection.classList.remove('d-none');
            tableBody.textContent = '';
            rows = [];
            if (!partyId) {
                emptyState.classList.add('d-none');
                loadingState.classList.add('d-none');
                return;
            }

            emptyState.classList.add('d-none');
            loadingState.classList.remove('d-none');

            fetch(config.loadUrl + '?' + (config.queryParam || 'id') + '=' + partyId, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
                .then(function (response) {
                    if (!response.ok) throw new Error('http');
                    return response.json();
                })
                .then(function (invoices) {
                    loadingState.classList.add('d-none');
                    if (partyNameLabel) partyNameLabel.textContent = partyName();
                    if (!invoices || invoices.length === 0) {
                        emptyState.classList.remove('d-none');
                        summary.badge.className = 'badge bg-secondary';
                        summary.badge.textContent = 'لا توجد فواتير غير مسددة';
                        return;
                    }
                    emptyState.classList.add('d-none');
                    openSection.classList.remove('d-none');
                    invoices.forEach(function (invoice) {
                        var row = buildRow(invoice);
                        rows.push(row);
                        tableBody.appendChild(row.checkbox.closest('tr'));
                    });
                    redistributeAll();
                })
                .catch(function () {
                    loadingState.classList.add('d-none');
                    if (summary.badge) {
                        summary.badge.className = 'badge bg-danger';
                        summary.badge.textContent = 'تعذر تحميل فواتير الطرف';
                    }
                });
        }

        var form = submitBtn.closest('form');
        form.addEventListener('submit', function (e) {
            e.preventDefault();
            var amountVal = amount();
            var allocated = 0;
            var checked = 0;
            var overRow = false;
            rows.forEach(function (row) {
                if (row.checkbox.checked) {
                    checked++;
                    var v = rowValue(row);
                    allocated += v;
                    if (v <= 0 || v > row.remaining + 0.005) overRow = true;
                }
            });
            rebuildAllocationNames();
            if (checked === 0) {
                Swal.fire({ icon: 'warning', title: 'لا يمكن الحفظ', text: 'يجب اختيار فاتورة واحدة على الأقل وتحديد مبلغ توزيع لها.', confirmButtonText: 'حسناً' });
                return;
            }
            if (overRow) {
                Swal.fire({ icon: 'warning', title: 'مبلغ توزيع غير صحيح', text: 'يوجد مبلغ توزيع أكبر من المتبقي على الفاتورة أو صفر. عدّل الأسطر ثم احفظ.', confirmButtonText: 'حسناً' });
                return;
            }
            if (Math.abs(amountVal - allocated) >= 0.005) {
                Swal.fire({ icon: 'warning', title: 'التوزيع غير متوازن', text: 'إجمالي المبالغ الموزعة (' + formatNumber(allocated) + ') يجب أن يساوي تماماً مبلغ الدفعة (' + formatNumber(amountVal) + '). عدّل الأسطر ثم احفظ.', confirmButtonText: 'حسناً' });
                return;
            }
            if (partySelect.value === '' || amountVal <= 0) {
                Swal.fire({ icon: 'warning', title: 'بيانات ناقصة', text: 'اختر الطرف وأدخل مبلغ الدفعة قبل الحفظ.', confirmButtonText: 'حسناً' });
                return;
            }
            Swal.fire({
                icon: 'question',
                title: config.confirmTitle,
                text: config.confirmText,
                showCancelButton: true,
                confirmButtonText: config.confirmButtonText || 'نعم، احفظ السند',
                cancelButtonText: 'إلغاء',
                confirmButtonColor: '#0f3460'
            }).then(function (result) {
                if (result.isConfirmed) {
                    form.submit();
                }
            });
        });

        if (partySelect) {
            partySelect.addEventListener('change', partyChanged);
            if (partySelect.value) partyChanged();
        }
        amountInput.addEventListener('input', redistributeAll);
        if (config.dateInputId) {
            document.getElementById(config.dateInputId).addEventListener('change', updateSummary);
        }

        updateSummary();
    };
})();
