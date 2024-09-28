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

// Function to toggle enable/disable
function toggleEnable(id) {
    fetch(`/RemoteScan?handler=ToggleEnable&id=${id}`, {
        method: 'POST',
        headers: {
            'X-Requested-With': 'XMLHttpRequest',
            'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value
        }
    })
        .then(response => response.json())
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
        .then(response => response.json())
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

// Function to update storage sources periodically
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
let expandedPaths = new Set();
let storageContentCache = null; // Cache for storage content
let currentNode = null; // Current node in the file explorer

// Variables to store sorting state
let sortColumn = 'name';
let sortOrder = 'asc';

// Function to open File Explorer modal
function openFileExplorer(selectedStorageId) {
    storageId = selectedStorageId;
    fetchFileExplorerData(storageId);
    $('#fileExplorerModal').modal('show');
}

// Function to close File Explorer modal
function closeFileExplorer() {
    $('#fileExplorerModal').modal('hide');
    expandedPaths.clear();
    storageContentCache = null;
    currentNode = null;
}

// Function to fetch File Explorer data
function fetchFileExplorerData(storageId) {
    $.ajax({
        url: '/RemoteScan?handler=FileExplorer',
        data: { storageId: storageId },
        method: 'GET',
        success: function (data) {
            if (data.success) {
                storageContentCache = data.storageContent;
                currentNode = storageContentCache;
                renderFileExplorer();
            } else {
                alert('Error: ' + data.message);
            }
        },
        error: function () {
            alert('Error loading file explorer data.');
        }
    });
}

// Function to render File Explorer
function renderFileExplorer(highlightFile = '') {
    try {
        const contentDiv = $('#fileExplorerContent');
        contentDiv.empty();

        const container = $('<div class="row"></div>');

        // Left column for directory navigation
        const leftCol = $('<div class="col-md-3 directory-navigation"></div>');
        const dirNav = $('<ul class="list-group" id="directoryTree"></ul>');

        // Populate the directory tree with immediate children of root
        if (storageContentCache.children && storageContentCache.children.length > 0) {
            // Sort directories alphabetically
            const sortedChildren = storageContentCache.children.slice().sort((a, b) => a.name.localeCompare(b.name));
            sortedChildren.forEach(childNode => {
                if (childNode.type === 'directory') {
                    childNode.parent = storageContentCache; // Set parent reference
                    const childLi = buildDirectoryTree(childNode);
                    dirNav.append(childLi);
                }
            });
        }

        leftCol.append(dirNav);

        // Right column for files and breadcrumb
        const rightCol = $('<div class="col-md-9"></div>');

        // Breadcrumb and back button
        const breadcrumb = buildBreadcrumb();
        rightCol.append(breadcrumb);

        // Files table container
        const fileTableContainer = $('<div id="fileTableContainer"></div>');
        rightCol.append(fileTableContainer);
        fileTableContainer.append(buildFileTable(highlightFile));

        container.append(leftCol);
        container.append(rightCol);
        contentDiv.append(container);
    } catch (error) {
        console.error('Error rendering File Explorer:', error);
        alert('An error occurred while rendering the File Explorer. Please try again.');
    }
}

// Function to build the directory tree
function buildDirectoryTree(node) {
    const li = $('<li class="list-group-item"></li>');
    const fullPath = node.fullPath;

    // Directory icon
    let folderIcon = $('<img src="/icons/folder.svg" class="directory-icon">');

    // Check if directory has child directories
    const hasChildDirectories = node.children && node.children.some(child => child.type === 'directory');

    // Placeholder for alignment
    const spacer = $('<span style="display:inline-block; width:16px;"></span>');

    let expandButton;
    let childUl;

    // Expand button or spacer
    if (hasChildDirectories) {
        expandButton = $('<button class="btn btn-sm btn-link chevron-button"><img src="/icons/chevron-right.svg" class="chevron-icon"></button>');
        li.append(expandButton);
    } else {
        li.append(spacer);
    }

    // Directory link
    const link = $('<a href="javascript:void(0);"></a>').append(folderIcon).append(' ' + node.name);
    link.click(function () {
        currentNode = node;
        renderFileExplorer();
    });
    li.append(link);

    // Child nodes
    if (hasChildDirectories) {
        childUl = $('<ul class="list-group"></ul>');
        // Sort child directories alphabetically
        const sortedChildren = node.children.slice().sort((a, b) => a.name.localeCompare(b.name));
        sortedChildren.forEach(childNode => {
            if (childNode.type === 'directory') {
                childNode.parent = node; // Set parent reference
                const childLi = buildDirectoryTree(childNode);
                childUl.append(childLi);
            }
        });
        li.append(childUl);

        // Set initial visibility
        if (expandedPaths.has(fullPath)) {
            childUl.show();
            expandButton.find('.chevron-icon').addClass('rotated');
            folderIcon.attr('src', '/icons/folder2-open.svg');
        } else {
            childUl.hide();
            expandButton.find('.chevron-icon').removeClass('rotated');
            folderIcon.attr('src', '/icons/folder.svg');
        }

        // Toggle functionality
        expandButton.click(function (e) {
            e.stopPropagation();
            if (childUl.is(':visible')) {
                childUl.slideUp('fast');
                expandButton.find('.chevron-icon').removeClass('rotated');
                folderIcon.attr('src', '/icons/folder.svg');
                expandedPaths.delete(fullPath);
            } else {
                childUl.slideDown('fast');
                expandButton.find('.chevron-icon').addClass('rotated');
                folderIcon.attr('src', '/icons/folder2-open.svg');
                expandedPaths.add(fullPath);
            }
        });
    }

    return li;
}

// Function to build the breadcrumb
function buildBreadcrumb() {
    const breadcrumb = $('<nav aria-label="breadcrumb"></nav>');
    const breadcrumbList = $('<ol class="breadcrumb align-items-center"></ol>');

    // Back button
    const backButton = $('<button class="btn btn-link p-0 mr-2" id="backButton"><img src="/icons/arrow-left-circle.svg" class="back-icon" alt="Back"></button>');
    backButton.click(function () {
        if (currentNode.parent) {
            currentNode = currentNode.parent;
            renderFileExplorer();
        }
    });
    breadcrumbList.append(backButton);

    // Disable back button if at root
    if (!currentNode.parent) {
        backButton.prop('disabled', true);
        backButton.find('img').addClass('inactive-icon');
    } else {
        backButton.prop('disabled', false);
        backButton.find('img').removeClass('inactive-icon');
    }

    // Build breadcrumb items
    let node = currentNode;
    const nodes = [];
    while (node) {
        nodes.unshift(node);
        node = node.parent;
    }

    nodes.forEach((node, index) => {
        const li = $('<li class="breadcrumb-item"></li>');
        if (index === nodes.length - 1) {
            // Current node (active)
            li.text(node.name);
        } else {
            // Ancestor nodes (clickable)
            const link = $('<a href="javascript:void(0);"></a>').text(node.name);
            link.click(function () {
                currentNode = node;
                renderFileExplorer();
            });
            li.append(link);
        }
        breadcrumbList.append(li);
    });

    breadcrumb.append(breadcrumbList);
    return breadcrumb;
}

function buildFileTable(highlightFile) {
    const fileTable = $('<table class="table table-striped"></table>');
    const tableHeader = $(`
        <thead>
            <tr>
                <th id="sort-name">Name <img src="/icons/caret-up-fill.svg" class="sort-icon"></th>
                <th id="sort-size">Size</th>
                <th id="sort-backup">Backup</th>
            </tr>
        </thead>
    `);
    fileTable.append(tableHeader);

    const tableBody = $('<tbody></tbody>');

    if (currentNode && currentNode.children) {
        // Combine directories and files
        let items = currentNode.children.map(item => {
            return {
                node: item,
                name: item.name,
                type: item.type,
                size: item.size,
                backupExists: item.backupExists,
                fullPath: item.fullPath
            };
        });

        // Sort the items
        items.sort((a, b) => {
            // Directories first
            if (a.type !== b.type) {
                return a.type === 'directory' ? -1 : 1;
            }

            let comparison = 0;
            if (sortColumn === 'name') {
                comparison = a.name.toLowerCase().localeCompare(b.name.toLowerCase());
            } else if (sortColumn === 'size') {
                comparison = (a.size || 0) - (b.size || 0);
            } else if (sortColumn === 'backup') {
                comparison = a.backupExists === b.backupExists ? 0 : a.backupExists ? -1 : 1;
            }

            return sortOrder === 'asc' ? comparison : -comparison;
        });

        items.forEach(item => {
            const row = $('<tr></tr>');
            const nameCell = $('<td></td>');

            if (item.type === 'directory') {
                // Directory icon and link
                const dirIcon = $('<img src="/icons/folder.svg" class="directory-table-icon">');
                const link = $('<a href="javascript:void(0);"></a>').append(dirIcon).append(' ' + item.name);
                link.click(function () {
                    item.node.parent = currentNode; // Set parent reference
                    currentNode = item.node;
                    renderFileExplorer();
                });
                nameCell.append(link);
            } else {
                // File icon
                const fileIcon = $('<img src="/icons/file-earmark.svg" class="file-table-icon">');
                nameCell.append(fileIcon).append(' ' + item.name);
            }

            row.append(nameCell);
            row.append('<td>' + (item.size !== undefined ? formatSize(item.size) : '') + '</td>');
            row.append('<td>' + (item.backupExists ? '<img src="/icons/check2.svg" class="badge-icon" alt="Yes">' : '<img src="/icons/x.svg" class="badge-icon" alt="No">') + '</td>');

            if (item.name === highlightFile) {
                row.addClass('highlighted-file');
            }

            tableBody.append(row);
        });
    }

    fileTable.append(tableBody);

    // Attach click handlers for sorting
    fileTable.find('#sort-name').click(function () {
        toggleSort('name');
        updateFileTable(highlightFile);
    });
    fileTable.find('#sort-size').click(function () {
        toggleSort('size');
        updateFileTable(highlightFile);
    });
    fileTable.find('#sort-backup').click(function () {
        toggleSort('backup');
        updateFileTable(highlightFile);
    });

    // Update sort icons
    updateSortIcons(fileTable);

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

    return fileTable;
}

function updateFileTable(highlightFile) {
    const fileTableContainer = $('#fileTableContainer');
    fileTableContainer.empty().append(buildFileTable(highlightFile));
}

function toggleSort(column) {
    if (sortColumn === column) {
        sortOrder = sortOrder === 'asc' ? 'desc' : 'asc';
    } else {
        sortColumn = column;
        sortOrder = 'asc';
    }
}

function updateSortIcons(fileTable) {
    fileTable.find('th img.sort-icon').remove();

    const sortIcon = $('<img class="sort-icon">').attr('src', sortOrder === 'asc' ? '/icons/caret-up-fill.svg' : '/icons/caret-down-fill.svg');

    if (sortColumn === 'name') {
        fileTable.find('#sort-name').append(' ').append(sortIcon);
    } else if (sortColumn === 'size') {
        fileTable.find('#sort-size').append(' ').append(sortIcon);
    } else if (sortColumn === 'backup') {
        fileTable.find('#sort-backup').append(' ').append(sortIcon);
    }
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

// Function to search files and directories from the cached data
function searchFiles(query) {
    if (!storageContentCache) {
        return;
    }

    const results = [];
    const maxResults = 10;
    searchInNode(storageContentCache, query.toLowerCase(), results, maxResults);

    renderSearchSuggestions(results);
}

// Recursive function to search within the node
function searchInNode(node, query, results, maxResults) {
    if (results.length >= maxResults) {
        return;
    }

    if (node.name.toLowerCase().includes(query)) {
        results.push({
            node: node,
            type: node.type.charAt(0).toUpperCase() + node.type.slice(1),
            name: node.name,
            fullPath: node.fullPath
        });
    }

    if (node.children && results.length < maxResults) {
        for (let child of node.children) {
            searchInNode(child, query, results, maxResults);
            if (results.length >= maxResults) {
                break;
            }
        }
    }
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
        let icon;
        if (result.type === "Directory") {
            icon = '<img src="/icons/folder.svg" class="directory-table-icon">';
        } else {
            icon = '<img src="/icons/file-earmark.svg" class="file-table-icon">';
        }
        const path = '<small class="text-muted"> [' + result.fullPath.replace(storageContentCache.fullPath, '') + ']</small>';
        item.html(icon + ' ' + result.name + ' ' + path);
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
        currentNode = result.node;
        renderFileExplorer();
    } else if (result.type === 'File') {
        currentNode = result.node.parent;
        renderFileExplorer(result.name);
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
