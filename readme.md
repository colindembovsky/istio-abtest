# A/B Testing in k8s using istio
This demo repo is meant as a POC for performing A/B testing of k8s services via [istio](istio.io) traffic routing.

## Components
There are several components to this repo:
1. service
    - this is a .NET Core API service that just has a single method (/api/version) that returns the value of the environment value `IMAGE_TAG` (this is set in the yaml files so that value of this environment variable is the same as the image tag)
    - this is only used to be able to distinguish different versions of the application
1. k8s
    - yaml files for experimenting with istio resources such as `virtualService`, `gateway` and `destinationRule`
    - you can look at these, but should really use the helm charts
1. helm
    - helm charts for deploying the app and setting up the istio components
    - this is how you should deploy the app as well as update the deployment and perform traffic routing
1. console
    - for testing the app
    - `load` test will issue 1000 calls to a URL and categorize the results: we use this to check the traffic routing rules
    - `continual` test will continually call the URL: we use this to ensure there is 0 downtime
    - also used to test for the performance impact of the istio mesh

## Prerequisites
1. You have to have a k8s cluster
    - for the examples below, we use [docker-for-windows](https://docs.docker.com/v17.09/docker-for-windows/install/), but [docker-for-mac](https://docs.docker.com/v17.12/docker-for-mac/install/) or [minikube](https://kubernetes.io/docs/tasks/tools/install-minikube/) would work too.
    > Note: you need the edge channel of docker-for-desktop in order to get k8s
    - the same concepts apply for remote k8s clusters - ports and IPs may differ though
1. Helm
    - you need to have [installed helm](https://docs.helm.sh/using_helm/) in your k8s cluster
1. .NET Core
    - Linux install instructions [here](https://docs.microsoft.com/en-us/dotnet/core/linux-prerequisites?tabs=netcore2x).

## Install istio
[Istio](istio.io) is a complex project - fortunately, the install is fairly simple. Full setup instructions are [here](https://istio.io/docs/setup/kubernetes/) but you can follow these steps to get a default install up quickly on your local cluster.

> Note: This install method is not secured, so don't use this method for production installs!

```sh
cd ~/
# download the latest istio release
curl -L https://git.io/getLatestIstio | sh -
cd istio-1.0.2  # version number may differ
# install custom resource definitions if helm < 2.10.0, otherwise skip this step
kubectl apply -f install/kubernetes/helm/istio/templates/crds.yaml
# wait for the CRDs to be applied

# install services via helm
# enable nodePort gateway and tracing
helm install install/kubernetes/helm/istio --name istio --namespace istio-system --set gateways.istio-ingressgateway.type=NodePort --set gateways.istio-egressgateway.type=NodePort --set tracing.enabled=true
```
Verify that the istio services are running by following [these instructions](https://istio.io/docs/setup/kubernetes/quick-start/#verifying-the-installation)

## Build the Service Image
To build the service image, just use `docker build`. We'll tag the image with 3 versions so that we can experiment with A/B routing.

```sh
cd service
# build the image
docker build -t col/api:1.0.0 .
# get the image id
export imageid=$(docker images | grep col | awk 'print {$3}')
# tag the image with additional versions
docker tag $imageid col/api:1.0.1
docker tag $imageid col/api:1.0.2
# check that we have 3 images:
docker images | grep col
# you should see something like this:
col/api   1.0.0    35af2c13521b   1 min ago   255 MB
col/api   1.0.1    35af2c13521b   1 min ago   255 MB
col/api   1.0.2    35af2c13521b   1 min ago   255 MB
```

## Create the Service via Helm
Now we can create the service and test the A/B traffic routing. For istio to perform traffic routing, pods require a sidecar. Fortunately we can tell istio to autoinject the sidecar so we can keep this plumbing out of our application code. Let's enable autoinjection of the sidecar for a namespace called `col`.

```sh
# create a namespace
kubectl create ns col
# label the namespace to enable autoinjection of the istio sidecar
kubectl label ns col istio-injection=enabled
```

Now we can install the service. The default chart installs 2 deployments - one for version 1.0.0 of the service and one for version 1.0.1. The traffic rule routes 100% of traffic to the "blue" deployment (1.0.0) initially.

```sh
# cd to repo root
helm install --name cols-api --namespace col helm/cols-api --set releaseNumber=1.0.1
# get the state of the pods and the value of the tag label
kubectl get po -n col -Ltag

# ouput should be something like this:
NAME                             READY     STATUS    RESTARTS   AGE       TAG
cols-api-blue-dcf4cd457-48gjm    2/2       Running   0          1m        1.0.0
cols-api-green-5d4dc85d4-26j95   2/2       Running   0          1m        1.0.1
```

Make sure you wait for the pods to be READY 2/2 and STATUS Running.

> Note: There are 2 containers in each pod because istio injected the sidecar for us.

Test that the service and istio gateway are working correctly:
```sh
curl http://localhost:31380/api/version
1.0.0⏎
```

## A/B Testing
There are 2 deployments at the moment: `cols-api-blue` and `cols-api-green`. Let's check the deployments and the `tag` label:

```sh
kubectl get deploy -n col -Ltag
NAME             DESIRED   CURRENT   UP-TO-DATE   AVAILABLE   AGE       TAG
cols-api-blue    1         1         1            1           55m       1.0.0
cols-api-green   1         1         1            1           55m       1.0.1
```

Even though the green deployment is running version 1.0.1, we are only getting responses from version 1.0.0 since the gateway is routing 100% of traffic to the blue deployment. Let's test this by running the `console` in `load test` mode:

```sh
cd console
dotnet run load http://localhost:32012/api/version
Starting load test
Time: 2937 ms
Total calls: 984
  -- version [1.0.0], percentage [100]
Average call response time: 2.98475609756098 ms
```

Even though there were some transient errors (16 in fact) we see that the only response we're getting from the gateway is version 1.0.0.

Let's route 20% of traffic to the new version (in the green deployment) and then re-run the console test:

```sh
helm upgrade cols-api --set canary[0].name=blue,canary[0].tag=1.0.0,canary[0].weight=80,canary[1].name=green,canary[1].tag=1.0.1,canary[1].weight=20 cols-api/ --set releaseNumber=1.0.1

# run the load test
dotnet run load http://localhost:32012/api/version
Starting load test
Time: 3395 ms
Total calls: 984
  -- version [1.0.0], percentage [80.2845528455285]
  -- version [1.0.1], percentage [19.7154471544715]
Average call response time: 3.45020325203252 ms
```
We're getting 20% of our traffic routed to the green deployment! If you `curl` the gateway URL again, you'll get `1.0.0` around 80% of the time, and `1.0.1` around 20% of the time.

Imagine we've now done our testing and we're happy with version 1.0.1. Let's update the deployment to route 100% of the traffic to the green deployment:

```sh
helm upgrade cols-api --set canary[0].name=blue,canary[0].tag=1.0.0,canary[0].weight=0,canary[1].name=green,canary[1].tag=1.0.1,canary[1].weight=100 cols-api/ --set releaseNumber=1.0.1

# run the load test
dotnet run load http://localhost:32012/api/version
Starting load test
Time: 5364 ms
Total calls: 982
  -- version [1.0.1], percentage [100]
Average call response time: 5.46232179226069 ms
```

Let's deploy version 1.0.2 of the app. Before we do so, we must note that the istio gateway is not like k8s services that will only begin routing traffic to ready deployments. If we route traffic to a brand new deployment straight away, any traffic to that deployment will fail while the deployment spins up. So we need to deploy the new deployment and route 0% traffic to it initially!

Let's update the blue deployment (which has 0% traffic routed to it) to the new version of the app:

```sh
helm upgrade cols-api --set canary[0].name=blue,canary[0].tag=1.0.2,canary[0].weight=0,canary[1].name=green,canary[1].tag=1.0.1,canary[1].weight=100 cols-api/ --set releaseNumber=1.0.2

# check the deployment
NAME             DESIRED   CURRENT   UP-TO-DATE   AVAILABLE   AGE       TAG
cols-api-blue    1         2         1            1           1h        1.0.2
cols-api-green   1         1         1            1           1h        1.0.1
```

> Note: the `CURRENT` count for `cols-api-blue` is 2 - this is because k8s is spinning up a 1.0.2 pod but the 1.0.0 pod is still running. As soon as the 1.0.2 pod is ready, the `CURRENT` count will drop to 1 again. Only at this point can we safely route traffic to this deployment. Before we do that, let's ensure that all our traffic is still going to the green deployment (currently on 1.0.1)

```sh
# run the load test
dotnet run load http://localhost:32012/api/version
Starting load test
Time: 3414 ms
Total calls: 990
  -- version [1.0.1], percentage [100]
Average call response time: 3.44848484848485 ms
```

Let's now update the traffic to route 20% of traffic to the blue deployment (1.0.2) so we can test that with a small amount of traffic:

```sh
helm upgrade cols-api --set canary[0].name=blue,canary[0].tag=1.0.2,canary[0].weight=20,canary[1].name=green,canary[1].tag=1.0.1,canary[1].weight=80 cols-api/ --set releaseNumber=1.0.2

# run the load test
dotnet run load http://localhost:32012/api/version
Starting load test
Time: 3008 ms
Total calls: 982
  -- version [1.0.2], percentage [20.4684317718941]
  -- version [1.0.1], percentage [79.5315682281059]
Average call response time: 3.06313645621181 ms
```

Now we can monitor our app and if we're happy, increase traffic to 100% to the blue deployment again. Then we could deploy 1.0.3 to the green deployment (which should still be receiving 0% traffic) and then route a small percentage of traffic to that deployment once the pods are up.

## Gotchas
When I was experimenting, the biggest gotcha I had was rolling out new deployments to a traffic route that was receiving traffic. In this case, the calls failed since the pods were not yet ready. Be aware that istio traffic routing makes no assumptions on the readiness of the target deployment!

To test this, run the console in `continual test` mode and perform the rollouts/traffic routing exercises before. You should always get a response even when rolling out new deployments (as long as they're rolling out to a route with 0% traffic).

```sh
dotnet run continual http://localhost:31380/api/version
Press ESC to stop
1.0.0
1.0.0
1.0.0
1.0.1
1.0.0
1.0.1
1.0.0
...
```

You can exit the run by pressing `Esc`.

## Performance
As a rough order of magnitude performance test, I wanted to see how long calls took when routed by istio vs native k8s service calls. As you see in the console tests above, most calls to the istio routed service took between 3 and 5ms.

Let's try the console directly against the service - we'll need to find the service nodePort first:

```sh
kubectl get svc -n col
NAME       TYPE       CLUSTER-IP       EXTERNAL-IP   PORT(S)        AGE
cols-api   NodePort   10.111.178.234   <none>        80:32012/TCP   1h

# looks like the nodePort is 32012
dotnet run load http://localhost:32012/api/version
Starting load test
Time: 2463 ms
Total calls: 987
  -- version [1.0.1], percentage [50.0506585612969]
  -- version [1.0.2], percentage [49.9493414387031]
Average call response time: 2.49544072948328 ms
```

We can see that we're getting even distribution for both deployments (this is exactly what k8s should be doing - the service selector labels that match both deploymets). We see that calls take around 2.5ms.

Conclusion? There is about 2ms of performance hit per call to the services in the mesh. This is by no means a comprehensive test, but we can expect some performance impact from istio.