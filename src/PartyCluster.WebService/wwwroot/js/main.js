function Api() {
    var self = this;
    this.appPath = '';
    this.refreshRate = 50000;
    this.serviceUrl = location.protocol + '//' + location.hostname + (location.port ? ':' + location.port : '') + self.appPath;

    this.GetClusters = function (onResult) {
        this.httpGetJson(self.serviceUrl + '/api/clusters', onResult)
    };

    this.JoinCluster = function (clusterId, useremail, result, failure) {
        $.ajax({
            url: self.serviceUrl + '/api/clusters/join/' + clusterId,
            type: 'POST',
            contentType: 'application/json',
            datatype: 'json',
            data: "{ UserEmail: '" + useremail + "' }"
        })
	   .done(function (data) {
	       result(data);
	   })
	   .fail(function (xhr) {
	       var json = $.parseJSON(xhr.responseText);
	       failure(json);
	       return;
	   });
    };

    this.JoinRandomCluster = function (userId, result, failure) {
        $.ajax({
            url: self.serviceUrl + '/api/clusters/joinRandom/' + userId,
            type: 'POST',
            contentType: 'application/json',
            datatype: 'json',
            data: "{ userId: '" + userId + "' }"
        })
	   .done(function (data) {
	       result(data);
	   })
	   .fail(function (xhr) {
	       var json = $.parseJSON(xhr.responseText);
	       failure(json);
	       return;
	   });
    };

    this.GetPartyStatus = function (userId, result, failure) {
        $.ajax({
            url: self.serviceUrl + '/api/clusters/partyStatus/' + userId,
            type: 'POST',
            contentType: 'application/json',
            datatype: 'json',
            data: "{ userId: '" + userId + "' }"
        })
	   .done(function (data) {
	       result(data);
	   })
	   .fail(function (xhr) {
	       var json = $.parseJSON(xhr.responseText);
	       failure(json);
	       return;
	   });
    };

    this.httpGetJson = function (url, result) {
        $.ajax({
            url: url,
            type: 'GET',
            contentType: 'application/json',
            datatype: 'json',
            cache: false
        })
	   .done(function (data) {
	       result(data);
	   })
	   .fail(function () {
	       $.ajax(this);
	       return;
	   });
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
    this.clusterList;
    this.userView;
    this.userId = '';
    this.clusterConnectionPort = 19000;
    this.clusterHttpGatewayPort = 19080;

    this.Initialize = function () {
        this.UpdateClusterList();
        $('.party-now-button').click(function () {
            self.JoinRandomCluster();
        });

        $('.join-now-button').click(function () {
            self.JoinPartyCluster(3);
        });

        $('.join-cluster-dialog').keypress(function (e) {
            // handle enter key press
            if (e.which == 13) {
                $('.join-now-button').click();
            }
        });


        setInterval(this.UpdateClusterList, self.refreshRate);
    };

    this.JoinRandomCluster = function () {
        this.ShowJoinClusterDialog();
    }

    this.JoinPartyCluster = function (retryCount) {
        var joinClusterWindow = $('.join-cluster-dialog');
        var joinClusterProgressWindow = $('.join-cluster-progress');

        var userId = $('#join-useremail').val();
        if (userId == null || userId == '') {
            return;
        }
        self.userId = userId;

        self.joinClusterDialog.Change(joinClusterWindow, joinClusterProgressWindow);

        self.api.JoinRandomCluster(
            userId,
            function (data) {
                self.joinClusterDialog.Hide(joinClusterProgressWindow);
                self.userView = data;
                self.DisplayPartyJoined();
            },
            function (data) {
                if (retryCount > 0) {
                    switch (data.Code) {
                        case "ClusterFull":
                        case "ClusterExpired":
                        case "ClusterNotReady":
                        case "ClusterDoesNotExist":
                        case "NoPortsAvailable":
                            self.JoinRandomCluster(retryCount - 1);
                            return;
                    }
                }

                if (self.IsPartyJoined(data)) {
                    self.joinClusterDialog.Hide(joinClusterProgressWindow);
                    self.DisplayPartyJoined();
                }
                else {
                    // TODO: handle failure
                }
            }
        );
    }

    this.ShowJoinClusterDialog = function () {
        var joinClusterWindow = $('.join-cluster-dialog');
        self.joinClusterDialog.Show(joinClusterWindow);
    };

    this.UpdateClusterList = function () {
        self.api.GetPartyStatus(
            self.userId,
            function (result) {
                self.userView = result;

                if (self.IsPartyClosed(self.userView)) {
                    self.DisplayPartyClosed();
                }
                else if (self.IsPartyJoined(self.userView)) {
                    self.DisplayPartyJoined();
                }
                else if (self.IsPartyOpen(self.userView)) {
                    self.DisplayPartyOpen();
                }
            },
            function (error) {
                // TODO: handle error
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

    this.DisplayPartyOpen = function () {
        $('#party-open-section').show();
        $('#party-closed-section').hide();
        $('#party-joined-section').hide();
    }

    this.DisplayPartyClosed = function () {
        $('#party-open-section').hide();
        $('#party-closed-section').show();
        $('#party-joined-section').hide();
    }

    this.DisplayPartyJoined = function () {
        $('#party-open-section').hide();
        $('#party-closed-section').hide();
        $('#party-joined-section').show();

        this.DisplayActiveClusterInformation();
    }

    this.DisplayActiveClusterInformation = function () {
        $('#cluster-details-endpoint').text(self.userView.ConnectionEndpoint);
        $('#cluster-details-userPort').text(self.userView.UserPort);
        $('#cluster-details-timeRemaining').text(self.userView.TimeRemaining);
        $('#cluster-details-expTime').text(self.userView.ExpirationTime);

        var uriPart = self.userView.ConnectionEndpoint.replace(self.clusterConnectionPort, self.clusterHttpGatewayPort);
        var uri = 'http://' + uriPart + '/Explorer/index.html';
        $('#sfe-link').text(uri);
        $('#sfe-link').attr('href', uri);
    }
};