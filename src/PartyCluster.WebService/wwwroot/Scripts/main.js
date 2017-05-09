function Api() {
    var self = this;
    this.appPath = '';
    this.refreshRate = 50000;
    this.serviceUrl = location.protocol + '//' + location.hostname + (location.port ? ':' + location.port : '') + self.appPath;

    this.JoinRandomCluster = function (result, failure, authorize) {
        $.ajax({
            url: self.serviceUrl + '/api/clusters/joinRandom/',
            type: 'POST',
            contentType: 'application/json',
            datatype: 'json',
            data: "{ userId: '' }"
        })
	   .done(function (data, status, xhr) {
	       self.handleResponse(data, status, xhr, result, failure, authorize);
	   })
	   .fail(function (xhr) {
	       self.handleFailure(xhr, failure);
	   });
    };

    this.GetPartyStatus = function (result, failure, authorize) {
        $.ajax({
            url: self.serviceUrl + '/api/clusters/partyStatus',
            type: 'POST',
            contentType: 'application/json',
            datatype: 'json',
            data: "{ userId: '' }"
        })
	   .done(function (data, status, xhr) {
	       self.handleResponse(data, status, xhr, result, failure, authorize);
	   })
	   .fail(function (xhr) {
	       self.handleFailure(xhr, failure);
	   });
    };

    this.handleResponse = function (data, status, xhr, onSuccess, onFailure, onAuthorize) {
        var response = xhr.getResponseHeader("X-Responded-JSON");
        if (response != null) {
            var parsedResponse = JSON.parse(response);
            if (parsedResponse.status == 401) {
                var headers = parsedResponse.headers;
                if (headers != null) {
                    onAuthorize(headers.location);
                }
            }
            else if (parsedResponse.status == 200) {
                onSuccess(data);
            }
        }
        else if (data != null && data.Status != null) {
            onSuccess(data);
        }
        else {
            self.handleFailure(xhr, onFailure);
        }
    }

    this.handleFailure = function (xhr, onFail) {
        var json = null;
        if (xhr.responseText != null && xhr.responseText != '') {
            var json = $.parseJSON(xhr.responseText);
        }
        onFail(json);
    }

    this.SetCsrfHeader = function () {
        $(document).bind("ajaxSend", function (elm, xhr, settings) {
            if (settings.type == 'POST' || settings.type == 'PUT' || settings.type == 'DELETE') {
                xhr.setRequestHeader("x-csrf-token", self.GetCookie('csrf-token'));
            }
        });
    }

    this.GetCookie = function (name) {
        var cookieValue = null;
        if (document.cookie && document.cookie != '') {
            var cookies = document.cookie.split(';');
            for (var i = 0; i < cookies.length; i++) {
                var cookie = jQuery.trim(cookies[i]);
                if (cookie.substring(0, name.length + 1) == (name + '=')) {
                    cookieValue = decodeURIComponent(cookie.substring(name.length + 1));
                    break;
                }
            }
        }
        return cookieValue;
    }
};

function Dialog() {

    var overlay = $('.overlay');

    this.Show = function ($modal) {
        overlay.fadeIn(300);
        $modal.fadeIn(300);
    };

    this.Hide = function ($modal) {
        overlay.fadeOut(200);
        $modal.fadeOut(200);
    };

    this.Change = function ($from, $to) {
        $from.fadeOut(200);
        $to.fadeIn(300);
    };

    overlay.click(function () {
        $(this).fadeOut(200);
        $('.dialog').fadeOut(200);
    });
};

function PartyClusters(api) {
    var self = this;
    this.api = api;
    this.refreshRate = 5000;
    this.joinClusterDialog = new Dialog();
    this.clusterConnectionPort = 19000;
    this.clusterHttpGatewayPort = 19080;

    this.Initialize = function () {
        self.api.SetCsrfHeader();
        self.UpdatePartyStatus();

        $('.party-now-button').click(function () {
            self.AuthenticateUser('');
        });

        $('.join-now-button').click(function () {
            self.JoinRandomCluster();
        });
    };

    this.JoinRandomCluster = function () {
        var joinClusterProgressWindow = $('.join-cluster-progress');
        var joinClusterFailedWindow = $('.join-cluster-dialog-failed');

        self.joinClusterDialog.Show(joinClusterProgressWindow);

        self.api.JoinRandomCluster(
            function (userView) { // success
                self.joinClusterDialog.Hide(joinClusterProgressWindow);
                self.DisplayPartyJoined(userView);
            },
            function (data) { // failure
                switch (data.Code) {
                    case "ClusterFull":
                    case "ClusterExpired":
                    case "ClusterNotReady":
                    case "ClusterDoesNotExist":
                    case "NoPortsAvailable":
                        self.joinClusterDialog.Change(joinClusterProgressWindow, joinClusterFailedWindow);
                        self.DisplayPartyOpen();
                        return;
                }

                if (self.IsPartyJoined(data)) {
                    self.joinClusterDialog.Hide(joinClusterProgressWindow);
                    self.DisplayPartyJoined();
                }
                else {
                    self.joinClusterDialog.Change(joinClusterProgressWindow, joinClusterFailedWindow);
                    self.DisplayPartyOpen();
                }
            },
            function (loginUrl) { // authorize
                self.joinClusterDialog.Change(joinClusterProgressWindow, joinClusterFailedWindow);
                self.DisplayPartyOpen();
            }
        );
    }

    this.UpdatePartyStatus = function () {
        self.api.GetPartyStatus(
            function (userView) {
                if (self.IsPartyClosed(userView)) {
                    self.DisplayPartyClosed(userView);
                }
                else if (self.IsPartyJoined(userView)) {
                    self.DisplayPartyJoined(userView);
                }
                else if (self.IsPartyOpen(userView)) {
                    self.DisplayPartyOpen(userView);
                }
            },
            function (error) {
                // TODO: handle error
            },
            function (loginUrl) { // authorize
                self.DisplayPartyUnauthenticated();
            });
    };

    this.IsPartyOpen = function (userViewInstance) {
        return userViewInstance.Status == "Open";
    }

    this.IsPartyClosed = function (userViewInstance) {
        return userViewInstance.Status == "Closed";
    }

    this.IsPartyJoined = function (userViewInstance) {
        return userViewInstance.Status == "Joined";
    }

    this.DisplayPartyOpen = function (userView) {
        $('#party-unauth-section').hide();
        $('#party-open-section').show();
        $('#party-closed-section').hide();
        $('#party-joined-section').hide();

        self.DisplayAuthHeader(userView);
    }

    this.DisplayPartyClosed = function (userView) {
        $('#party-unauth-section').hide();
        $('#party-open-section').hide();
        $('#party-closed-section').show();
        $('#party-joined-section').hide();

        self.DisplayAuthHeader(userView);
    }

    this.DisplayPartyJoined = function (userView) {
        $('#party-unauth-section').hide();
        $('#party-open-section').hide();
        $('#party-closed-section').hide();
        $('#party-joined-section').show();

        self.DisplayAuthHeader(userView);
        self.DisplayActiveClusterInformation(userView);
    }

    this.DisplayPartyUnauthenticated = function (userView) {
        $('#party-unauth-section').show();
        $('#party-open-section').hide();
        $('#party-closed-section').hide();
        $('#party-joined-section').hide();
    }

    this.DisplayActiveClusterInformation = function (userView) {
        $('#cluster-details-endpoint').text(userView.ConnectionEndpoint);
        $('#cluster-details-userPort').text(userView.UserPort);
        $('#cluster-details-timeRemaining').text(userView.TimeRemaining);
        $('#cluster-details-expTime').text(userView.ExpirationTime);

        var uriPart = userView.ConnectionEndpoint.replace(self.clusterConnectionPort, self.clusterHttpGatewayPort);
        var uri = 'http://' + uriPart + '/Explorer/index.html';
        $('#sfe-link').text(uri);
        $('#sfe-link').attr('href', uri);
    }

    this.DisplayAuthHeader = function (userView) {
        if (userView != null && userView.UserId != null) {
            $('#party-auth-userId').text(userView.UserId);
            $('#party-area-auth-header').show();
        }
        else {
            $('#party-auth-userId').text('');
            $('#party-area-auth-header').hide();
        }
    }

    this.AuthenticateUser = function () {
        $('#auth-fb-link').attr('href', "/auth/facebook");
        $('#auth-github-link').attr('href', "/auth/github");

        var joinClusterAuthWindow = $('.join-cluster-authenticate');
        self.joinClusterDialog.Show(joinClusterAuthWindow);
    }
};