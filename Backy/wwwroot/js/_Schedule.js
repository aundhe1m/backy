
// Index Schedule functionality
let currentStorageId = null;

function openScheduleModal(storageId) {
    currentStorageId = storageId;
    $('#scheduleModal').modal('show');
    loadSchedules();
}

function closeScheduleModal() {
    $('#scheduleModal').modal('hide');
    currentStorageId = null;
}

function loadSchedules() {
    $.ajax({
        url: '/RemoteScan?handler=GetIndexSchedules',
        data: { id: currentStorageId },
        method: 'GET',
        success: function (data) {
            if (data.success) {
                renderSchedules(data.schedules);
            } else {
                alert('Error loading schedules.');
            }
        },
        error: function () {
            alert('Error loading schedules.');
        }
    });
}

function renderSchedules(schedules) {
    const tableBody = $('#scheduleTableBody');
    tableBody.empty();

    schedules.forEach(schedule => {
        const row = createScheduleRow(schedule);
        tableBody.append(row);
    });
}

function createScheduleRow(schedule = null) {
    const days = ['mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun'];
    const row = $('<tr></tr>');

    days.forEach((day, index) => {
        const dayCell = $('<td></td>');
        const checkbox = $('<input type="checkbox">').attr('data-day', index);

        if (schedule && schedule.Days.includes(index)) {
            checkbox.prop('checked', true);
        }

        dayCell.append(checkbox);
        row.append(dayCell);
    });

    const timeCell = $('<td></td>');
    const timeInput = $('<input type="time">').addClass('form-control').val(schedule ? schedule.Time : '');
    timeCell.append(timeInput);
    row.append(timeCell);

    const deleteCell = $('<td></td>');
    const deleteButton = $('<button type="button" class="btn btn-transparent-warning"><img src="/icons/trash.svg" alt="Delete"></button>');
    deleteButton.click(function () {
        row.remove();
    });
    deleteCell.append(deleteButton);
    row.append(deleteCell);

    return row;
}

function addScheduleRow() {
    const row = createScheduleRow();
    $('#scheduleTableBody').append(row);
}

function saveSchedules() {
    const schedules = [];
    $('#scheduleTableBody tr').each(function () {
        const row = $(this);
        const days = [];
        row.find('input[type="checkbox"]').each(function () {
            if ($(this).is(':checked')) {
                days.push(parseInt($(this).attr('data-day')));
            }
        });
        const time = row.find('input[type="time"]').val();
        if (days.length > 0 && time) {
            schedules.push({ Days: days, Time: time });
        }
    });

    $.ajax({
        url: '/RemoteScan?handler=SaveIndexSchedules',
        method: 'POST',
        data: JSON.stringify({ StorageId: currentStorageId, Schedules: schedules }),
        contentType: 'application/json',
        headers: {
            'RequestVerificationToken': $('input[name="__RequestVerificationToken"]').val(),
            'X-Requested-With': 'XMLHttpRequest' // Ensure it's treated as an AJAX request
        },
        success: function (data) {
            if (data.success) {
                showToast(`Schedules saved successfully.`, true);
                closeScheduleModal();
            } else {
                showToast(`Error saving schedules: ${data.message}`, false);
            }
        },
        error: function () {
            showToast(`Error saving schedules: ${data.message}`, false);
        }
    });
}