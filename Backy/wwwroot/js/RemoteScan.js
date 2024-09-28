// Function to format size in bytes to human-readable format
function formatSize(sizeInBytes) {
    if (sizeInBytes === null || sizeInBytes === 0) return '0 B';

    let size = sizeInBytes;
    const suffixes = ['B', 'KB', 'MB', 'GB', 'TB'];
    let suffixIndex = 0;

    while (size >= 1024 && suffixIndex < suffixes.length - 1) {
        size /= 1024;
        suffixIndex++;
    }

    return size.toFixed(2) + ' ' + suffixes[suffixIndex];
}

// Initialize tooltips
document.addEventListener('DOMContentLoaded', function () {
    var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
    tooltipTriggerList.map(function (tooltipTriggerEl) {
        return new bootstrap.Tooltip(tooltipTriggerEl);
    });
});

// Handle expand/collapse chevron rotation
document.querySelectorAll('[data-bs-toggle="collapse"]').forEach(function (toggleButton) {
    var targetId = toggleButton.getAttribute('data-bs-target');
    var collapseElement = document.querySelector(targetId);

    if (collapseElement && toggleButton) {
        // Initialize collapsed state
        toggleButton.classList.add('collapsed');

        collapseElement.addEventListener('shown.bs.collapse', function () {
            toggleButton.classList.remove('collapsed');
        });

        collapseElement.addEventListener('hidden.bs.collapse', function () {
            toggleButton.classList.add('collapsed');
        });
    }
});

// Function to toggle enable/disable
function toggleEnable(id) {
    fetch(`/RemoteScan?handler=ToggleEnable&id=${id}`, {
        method: 'POST',
        headers: {
            'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
        }
    })
        .then(response => {
            if (!response.ok) {
                throw new Error('Network response was not ok');
            }
            return response.json();
        })
        .then(data => {
            if (data.success) {
                showToast(`Storage status changed.`, true);
            } else {
                showToast(`Storage failed to change: ${data.message}`, false);
            }
        })
        .catch(error => {
            console.error('There was a problem with the fetch operation:', error);
            showToast(`An error occurred while updating the storage status: ${error}`, false);
        });
}


// Function to start indexing
function startIndexing(id) {
    const startIndexingButton = document.getElementById(`startIndexingButton-${id}`);
    const startIndexingIcon = document.getElementById(`startIndexingIcon-${id}`);

    // Disable the button and add rotating class to icon
    if (startIndexingButton && startIndexingIcon) {
        startIndexingButton.disabled = true;
        startIndexingIcon.classList.add('rotating');
    }

    fetch(`/RemoteScan?handler=StartIndexing&id=${id}`, {
        method: 'POST',
        headers: {
            'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
        }
    })
        .then(response => {
            if (!response.ok) {
                throw new Error('Network response was not ok');
            }
            return response.json();
        })
        .catch(error => {
            console.error('There was a problem with the fetch operation:', error);
            // Re-enable the button and remove rotating class
            if (startIndexingButton && startIndexingIcon) {
                startIndexingButton.disabled = false;
                startIndexingIcon.classList.remove('rotating');
                showToast(`Indexing failed: ${error}`, false);
            }
        });
}


function updateStorageSources() {
    fetch('/RemoteScan?handler=UpdateStorageSources')
        .then(response => response.json())
        .then(data => {
            if (data.success) {
                data.storageSources.forEach(source => {
                    // Update BackupPercentage
                    const backupPercentageElement = document.getElementById(`backupPercentage-${source.id}`);
                    if (backupPercentageElement) {
                        backupPercentageElement.textContent = `${source.backupPercentage}% Backup`;
                    }

                    // Update progress bar
                    const progressBarElement = document.getElementById(`progressBar-${source.id}`);
                    if (progressBarElement) {
                        progressBarElement.style.width = `${source.backupPercentage}%`;
                        progressBarElement.setAttribute('aria-valuenow', source.backupPercentage);
                    }

                    // Update File and Backup Info table
                    const totalFilesElement = document.getElementById(`totalFiles-${source.id}`);
                    if (totalFilesElement) {
                        totalFilesElement.textContent = source.totalFiles;
                    }

                    const backupCountElement = document.getElementById(`backupCount-${source.id}`);
                    if (backupCountElement) {
                        backupCountElement.textContent = source.backupCount;
                    }

                    const totalSizeElement = document.getElementById(`totalSize-${source.id}`);
                    if (totalSizeElement) {
                        totalSizeElement.textContent = formatSize(source.totalSize);
                    }

                    const totalBackupSizeElement = document.getElementById(`totalBackupSize-${source.id}`);
                    if (totalBackupSizeElement) {
                        totalBackupSizeElement.textContent = formatSize(source.totalBackupSize);
                    }

                    // Update IsIndexing state
                    const startIndexingButton = document.getElementById(`startIndexingButton-${source.id}`);
                    const startIndexingIcon = document.getElementById(`startIndexingIcon-${source.id}`);
                    if (startIndexingButton && startIndexingIcon) {
                        if (source.isIndexing) {
                            startIndexingButton.disabled = true;
                            startIndexingIcon.classList.add('rotating');
                        } else {
                            startIndexingButton.disabled = false;
                            startIndexingIcon.classList.remove('rotating');
                        }
                    }
                });
            }
        })
        .catch(error => console.error('Error updating storage sources:', error));
}

setInterval(updateStorageSources, 5000);

// File Explorer functionality
let storageId = null;

function openFileExplorer(selectedStorageId) {
    storageId = selectedStorageId;
    fetchFileExplorerData(storageId, null);
    $('#fileExplorerModal').modal('show');
}

function closeFileExplorer() {
    $('#fileExplorerModal').modal('hide');
}

function fetchFileExplorerData(storageId, path, searchQuery = '', highlightFile = '') {
    $.ajax({
        url: '/RemoteScan?handler=GetFileExplorer',
        data: { storageId: storageId, path: path },
        method: 'GET',
        success: function (data) {
            if (data.success) {
                renderFileExplorer(data, highlightFile);
            } else {
                alert('Error: ' + data.message);
            }
        },
        error: function () {
            alert('Error loading file explorer data.');
        }
    });
}

function renderFileExplorer(data, highlightFile = '') {
    try {
        const contentDiv = $('#fileExplorerContent');
        contentDiv.empty();

        const container = $('<div class="row"></div>');

        // Left column for directory navigation
        const leftCol = $('<div class="col-md-3 directory-navigation"></div>');
        const dirNav = $('<ul id="directoryTree" class="list-group"></ul>');

        // Populate the directory tree
        if (data.directoryTree && data.directoryTree.children.length > 0) {
            data.directoryTree.children.forEach(function (childNode) {
                const childLi = renderDirectoryNode(childNode);
                dirNav.append(childLi);
            });
        } else {
            console.warn('No children found in directoryTree:', data.directoryTree);
        }

        leftCol.append(dirNav);

        // Right column for files and breadcrumb
        const rightCol = $('<div class="col-md-9"></div>');

        // Breadcrumb
        const breadcrumb = $('<nav aria-label="breadcrumb"></nav>');
        const breadcrumbList = $('<ol class="breadcrumb align-items-center"></ol>');

        const navPathSegments = data.navPath.split('/').filter(s => s.length > 0);
        const remotePath = data.remotePath;
        let cumulativeNavPath = '';
        let cumulativeFullPath = remotePath;

        // Back button
        const backButton = $('<button class="btn btn-link p-0 mr-2" id="backButton"><img src="/icons/arrow-left-circle.svg" class="back-icon" alt="Back"></button>');
        backButton.click(function () {
            if (!backButton.find('img').hasClass('inactive-icon')) {
                fetchFileExplorerData(storageId, data.parentPath);
            }
        });
        breadcrumbList.append(backButton);

        // Root link
        const rootLi = $('<li class="breadcrumb-item"></li>');
        const rootLink = $('<a href="javascript:void(0);"><i class="fa fa-home"></i> Root</a>');
        rootLink.click(function () {
            fetchFileExplorerData(storageId, remotePath);
        });
        rootLi.append(rootLink);
        breadcrumbList.append(rootLi);

        navPathSegments.forEach((segment, index) => {
            cumulativeNavPath += '/' + segment;
            cumulativeFullPath += '/' + segment;
            const currentFullPath = cumulativeFullPath; // Capture the current value
            if (index === navPathSegments.length - 1) {
                breadcrumbList.append($('<li class="breadcrumb-item active" aria-current="page">' + segment + '</li>'));
            } else {
                const li = $('<li class="breadcrumb-item"></li>');
                const link = $('<a href="javascript:void(0);"></a>').text(segment);
                link.click(function () {
                    fetchFileExplorerData(storageId, currentFullPath);
                });
                li.append(link);
                breadcrumbList.append(li);
            }
        });
        breadcrumb.append(breadcrumbList);
        rightCol.append(breadcrumb);

        // Update the back icon's opacity based on current path
        const backButtonImg = $('#backButton img');
        if (data.currentPath === data.remotePath) {
            backButtonImg.addClass('inactive-icon');
        } else {
            backButtonImg.removeClass('inactive-icon');
        }

        // Files table
        const fileTable = $('<table class="table table-striped"></table>');
        const tableHeader = $('<thead><tr><th>Name</th><th>Type</th><th>Size</th><th>Backup</th></tr></thead>');
        fileTable.append(tableHeader);

        const tableBody = $('<tbody></tbody>');

        // Directories in the right panel (optional)
        if (data.directories && data.directories.length > 0) {
            data.directories.forEach(dir => {
                const row = $('<tr></tr>');
                const nameCell = $('<td></td>');

                // Directory icon
                const dirIcon = $('<i class="fa fa-folder"></i>');
                const link = $('<a href="javascript:void(0);"></a>').append(dirIcon).append(' ' + dir.Name);
                link.click(function () {
                    fetchFileExplorerData(storageId, dir.FullPath);
                });
                nameCell.append(link);
                row.append(nameCell);
                row.append('<td>Directory</td>');
                row.append('<td>N/A</td>');
                row.append('<td>N/A</td>');
                tableBody.append(row);
            });
        }

        // Files
        data.files.forEach(file => {
            const row = $('<tr></tr>');
            const nameCell = $('<td></td>');

            // File icon
            const fileIcon = $('<i class="fa fa-file"></i>');

            nameCell.append(fileIcon).append(' ' + file.name);
            row.append(nameCell);
            row.append('<td>File</td>');

            // Size handled in JavaScript
            const sizeCell = $('<td></td>').text(formatSize(file.size));
            row.append(sizeCell);

            // Backup status
            let backupStatus;
            if (file.backupExists) {
                backupStatus = '<i class="fa fa-check-circle text-success" title="Backed Up"></i>';
            } else {
                backupStatus = '<i class="fa fa-times-circle text-danger" title="Pending Backup"></i>';
            }
            row.append('<td>' + backupStatus + '</td>');

            if (file.name === highlightFile) {
                row.addClass('highlighted-file');
            }

            tableBody.append(row);
        });

        fileTable.append(tableBody);
        rightCol.append(fileTable);

        container.append(leftCol);
        container.append(rightCol);
        contentDiv.append(container);

        // Initialize the directory tree if not already initialized
        if ($('#directoryTree').children().length === 0) {
            renderDirectoryTree(data.directoryTree);
        }

        // Scroll to highlighted file
        if (highlightFile !== '') {
            const highlightedRow = tableBody.find('.highlighted-file');
            if (highlightedRow.length > 0) {
                highlightedRow[0].scrollIntoView({ behavior: 'smooth', block: 'center' });

                // Remove the highlight after 3 seconds
                setTimeout(function () {
                    highlightedRow.removeClass('highlighted-file');
                }, 3000);
            }
        }

        // Format file sizes after rendering
        $('td[data-size]').each(function () {
            const sizeInBytes = parseInt($(this).attr('data-size'));
            if (!isNaN(sizeInBytes)) {
                $(this).text(formatSize(sizeInBytes));
            } else {
                $(this).text('N/A');
            }
        });

    } catch (error) {
        console.error('Error rendering File Explorer:', error);
        alert('An error occurred while rendering the File Explorer. Please try again.');
    }
}


// Function to render the directory tree in the left navigation
function renderDirectoryTree(directoryNode) {
    const directoryTree = $('#directoryTree');
    directoryTree.empty();

    if (directoryNode && directoryNode.children && directoryNode.children.length > 0) {
        directoryNode.children.forEach(childNode => {
            const childLi = renderDirectoryNode(childNode);
            directoryTree.append(childLi);
        });
    } else {
        console.warn('No children found in directoryTree:', directoryNode);
    }
}


// Recursive function to render a directory node
function renderDirectoryNode(node) {
    const li = $('<li class="list-group-item"></li>');

    // Create a collapsible button if the node has children
    if (node.children && node.children.length > 0) {
        const toggleButton = $('<button class="btn btn-sm btn-link chevron-button collapsed" data-bs-toggle="collapse"></button>');
        toggleButton.append('<i class="fa fa-chevron-right chevron-icon"></i>');
        toggleButton.click(function () {
            const icon = $(this).find('.chevron-icon');
            icon.toggleClass('fa-chevron-right fa-chevron-down');
        });
        li.append(toggleButton);
    } else {
        // Add spacing for alignment if there's no toggle button
        li.append('<span style="display:inline-block; width:24px;"></span>');
    }

    // Directory link with icon
    const dirIcon = $('<i class="fa fa-folder"></i>');
    const link = $('<a href="javascript:void(0);"></a>').append(dirIcon).append(' ' + node.name);
    link.click(function () {
        fetchFileExplorerData(storageId, node.fullPath);
    });
    li.append(link);

    // If the node has children, create a nested list
    if (node.children && node.children.length > 0) {
        const nestedUl = $('<ul class="list-group collapse"></ul>');
        node.children.forEach(childNode => {
            const childLi = renderDirectoryNode(childNode);
            nestedUl.append(childLi);
        });
        li.append(nestedUl);
    }

    return li;
}

function formatBytes(bytes) {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

// Search functionality
$('#searchInput').keyup(function () {
    const query = $(this).val();
    if (query.length >= 3) {
        searchFiles(query);
    } else {
        $('#searchSuggestions').hide();
    }
});

// Close suggestions when clicking outside
$(document).click(function (event) {
    if (!$(event.target).closest('#searchInput').length && !$(event.target).closest('#searchSuggestions').length && !$(event.target).closest('.input-group-append').length) {
        $('#searchSuggestions').hide();
    }
});

function searchFiles(query) {
    $.ajax({
        url: '/RemoteScan?handler=SearchFiles',
        data: { storageId: storageId, query: query },
        method: 'GET',
        success: function (data) {
            if (data.success) {
                renderSearchSuggestions(data.results);
            } else {
                $('#searchSuggestions').hide();
            }
        },
        error: function () {
            $('#searchSuggestions').hide();
        }
    });
}

function renderSearchSuggestions(results) {
    const suggestionsDiv = $('#searchSuggestions');
    suggestionsDiv.empty();

    if (results.length === 0) {
        suggestionsDiv.hide();
        return;
    }

    results.forEach(function (result) {
        const item = $('<a class="dropdown-item" href="javascript:void(0);"></a>');
        if (result.type === "Directory") {
            item.html('<i class="fa fa-folder"></i> ' + result.name + ' <span class="text-muted">[Directory]</span>');
        } else {
            item.html('<i class="fa fa-file"></i> ' + result.name + ' <span class="text-muted">[File]</span>');
        }
        item.click(function () {
            navigateToSearchResult(result);
            suggestionsDiv.hide();
        });
        suggestionsDiv.append(item);
    });

    suggestionsDiv.show();
}

function navigateToSearchResult(result) {
    $('#searchInput').val('');
    $('#searchSuggestions').hide();
    if (result.type === 'Directory') {
        // Navigate to the directory
        fetchFileExplorerData(storageId, result.fullPath);
    } else if (result.type === 'File') {
        // Navigate to the directory containing the file and highlight the file
        const directoryPath = result.fullPath.substring(0, result.fullPath.lastIndexOf('/'));
        fetchFileExplorerData(storageId, directoryPath, '', result.name);
    }
}

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

        if (schedule && schedule.days.includes(index)) {
            checkbox.prop('checked', true);
        }

        dayCell.append(checkbox);
        row.append(dayCell);
    });

    const timeCell = $('<td></td>');
    const timeInput = $('<input type="time">').addClass('form-control').val(schedule ? schedule.time : '');
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
            schedules.push({ days: days, time: time });
        }
    });

    $.ajax({
        url: '/RemoteScan?handler=SaveIndexSchedules',
        method: 'POST',
        data: JSON.stringify({ storageId: currentStorageId, schedules: schedules }),
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
