function Api() {
    var self = this;
    this.refreshRate = 5000;
    this.serviceUrl = location.protocol + '//' + location.hostname + (location.port ? ':' + location.port : '') + '/partyclusters';

    this.GetClusters = function (result) {
        this.httpGetJson(self.serviceUrl + '/api/clusters', result);
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

function SelectedCluster(id, name, autoRetry) {
    var self = this;
    this.Id = id;
    this.Name = name;
    this.AutoRetry = autoRetry;
}

function PartyClusters(api) {
    var self = this;
    this.api = api;
    this.refreshRate = 5000;
    this.joinClusterDialog = new Dialog();
    this.selectedCluster;

    this.Initialize = function () {
        this.PopulateClusterList();
        $('.party-now-button').click(function () {
            try {
                var id = self.SelectRandomCluster();
                self.ShowJoinClusterDialog(new SelectedCluster(id, 'Party now!', true));
            }
            catch (exception) {
                alert('There are currently no clusters available. Please try again later.');
            }
        });

        $('.join-now-button').click(function () {
            var joinClusterWindow = $('.join-cluster-dialog');
            var email = $('#join-useremail').val();

            self.api.JoinCluster(
				self.selectedCluser.Id,
				email,
				function (data) {
				    var joinClusterSuccessWindow = $('.join-cluster-dialog-success');
				    self.joinClusterDialog.Change(joinClusterWindow, joinClusterSuccessWindow);
				},
				function (data) {
				    if (self.selectedCluser.AutoRetry) {
				        switch (data.Code) {
				            case "ClusterFull":
				            case "ClusterExpired":
				            case "ClusterNotReady":
				            case "ClusterDoesNotExist":
				            case "NoPortsAvailable":
				                self.selectedClusterId = self.SelectRandomCluster();
				                setTimeout(self.JoinCluster, self.refreshRate)
				                return;
				        }
				    }

				    var failedClusterWindow = $('.join-cluster-dialog-failed');
				    switch (data.Code) {
				        case "InvalidArguments":
				            $('p', failedClusterWindow).text("Please provide a valid email address.");
				            break;
				        case "ClusterFull":
				            $('p', failedClusterWindow).text("Sorry, this cluster is full. Please try a different one.");
				            break;
				        case "ClusterExpired":
				            $('p', failedClusterWindow).text("Sorry, this cluster has expired and is being replaced with a new one. Please try a different one.");
				            break;
				        case "ClusterNotReady":
				            $('p', failedClusterWindow).text("Sorry, this cluster isn't ready yet. Please try a different in the meantime.");
				            break;
				        case "ClusterDoesNotExist":
				            $('p', failedClusterWindow).text("Sorry, that cluster doesn't exist! Please try a different one.");
				            break;
				        case "NoPortsAvailable":
				            $('p', failedClusterWindow).text("Sorry, there are no ports available for your application to use on this cluster. Please try a different one.");
				            break;
				        case "SendMailFailed":
				            $('p', failedClusterWindow).text("Sorry, we couldn't send an invitation to that email. Please check for typos or try a different email.");
				            break;
				        default:
				            $('p', failedClusterWindow).text("Sorry, something went wrong with this cluster. Please try a different one.");
				            break;
				    }

				    self.joinClusterDialog.Change(joinClusterWindow, failedClusterWindow);
				}
			);
        });

        setInterval(this.PopulateClusterList, self.refreshRate);
    };

    this.ShowJoinClusterDialog = function (selectedCluster) {
        var joinClusterWindow = $('.join-cluster-dialog');
        self.joinClusterDialog.Show(joinClusterWindow);
        self.selectedCluser = selectedCluster;

        $('h3', joinClusterWindow).text(selectedCluster.Name);
        $('#join-useremail', joinClusterWindow).val('');
    };

    this.SelectRandomCluster = function () {
        var tableRows = $('.cluster-list table tr');
        var clusterId = 0;
        var clusterName = '';
        for (var i = 0; i < tableRows.length; ++i) {
            var $row = $(tableRows[i]);
            if ($row.attr('data-users') < $row.attr('data-capacity')) {
                return $row.attr('data-id');
            }
        }
        throw "No clusters currently available";
    };


    this.PopulateClusterList = function () {
        self.api.GetClusters(function (data) {
            var clusterTable = $('.cluster-list table');
            clusterTable.empty();

            $('<thead/>')
               .append(
                   $('<tr/>')
                       .append(
                           $('<th/>').text('Name'))
                       .append(
                           $('<th/>').text('Users'))
                       .append(
                           $('<th/>').text('Applications'))
                       .append(
                           $('<th/>').text('Services'))
                       .append(
                           $('<th/>').text('Time left'))
                       .append(
                           $('<th/>').text(''))
                       )
               .appendTo(clusterTable);

            $('<tbody>')
				.appendTo(clusterTable);

            $.each(data, function (id, jObject) {
                $('<tr data-id="' + jObject.ClusterId + '" data-users="' + jObject.UserCount + '" data-capacity="' + jObject.Capacity + '" />')
                .append(
                    $('<td/>').text(jObject.Name))
                .append(
                    $('<td/>').text(jObject.UserCount))
                .append(
                    $('<td/>').text(jObject.AppCount))
                .append(
                    $('<td/>').text(jObject.ServiceCount))
                .append(
                    $('<td/>').text(jObject.TimeRemaining))
                .append(
                    $('<td/>')
                        .append(
                            $('<a href="javascript:void(0);" class="button">')
                                .text('Join!')
                                    .click(function () {
                                        self.ShowJoinClusterDialog(new SelectedCluster(jObject.ClusterId, jObject.Name, false));
                                    })))
            .appendTo(clusterTable);

            });
        });
    };
};
